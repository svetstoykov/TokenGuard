using System.Text.Json;

namespace Codexplorer.Automation.AgentComparison;

/// <summary>Retains successful fake-boundary assertions against exact native runtime bundles.</summary>
/// <remarks>This per-command service reads immutable receipts and never constructs an inference client.</remarks>
internal sealed class ComparisonFixtureEvidence : IComparisonFixtureEvidence
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly (string Runtime, string Scenario)[] Required =
    [
        ("codexplorer", "compaction"), ("codexplorer", "provider-failure"),
        ("opencode", "compaction"), ("opencode", "denied-tools"), ("opencode", "provider-failure")
    ];

    /// <inheritdoc />
    public async Task WriteAsync(string scenarioRoot, string runtimeExecutable, string runtime, string scenario)
    {
        var files = EnumerateEvidence(scenarioRoot).Where(path => Path.GetFileName(path) != "fixture-verified.json")
            .Select(path => new FileDigest(Path.GetRelativePath(scenarioRoot, path), ComparisonArtifacts.Hash(path))).ToArray();
        var receipt = new ComparisonFixtureReceipt(1, true, false, true, runtime, scenario,
            ComparisonBundleInventory.Digest(ComparisonBundleInventory.Files(Path.GetDirectoryName(runtimeExecutable)!)), files);
        await File.WriteAllTextAsync(Path.Combine(scenarioRoot, "fixture-verified.json"), JsonSerializer.Serialize(receipt, JsonOptions));
    }

    /// <inheritdoc />
    public IReadOnlyList<string> FindVerified(string root, IReadOnlyList<RuntimeBundle> bundles)
    {
        var receipts = new List<(string Path, ComparisonFixtureReceipt Receipt)>();
        foreach (var family in new[] { "adapter-runtime-verification", "codexplorer-runtime-verification" })
        {
            var familyRoot = Path.Combine(root, "scratch", family);
            if (!Directory.Exists(familyRoot))
                continue;
            foreach (var path in EnumerateEvidence(familyRoot).Where(path => Path.GetFileName(path) == "fixture-verified.json"))
            {
                try
                {
                    var receipt = JsonSerializer.Deserialize<ComparisonFixtureReceipt>(File.ReadAllText(path), JsonOptions);
                    if (receipt is { SchemaVersion: 1, Synthetic: true, LiveExecutionStarted: false, Passed: true })
                        receipts.Add((path, receipt));
                }
                catch (Exception exception) when (exception is JsonException or IOException)
                {
                }
            }
        }
        var evidence = new List<string>();
        foreach (var (runtime, scenario) in Required)
        {
            var bundle = bundles.Single(bundle => bundle.Name == runtime);
            var digest = ComparisonBundleInventory.Digest(ComparisonBundleInventory.Files(Path.GetDirectoryName(bundle.Executable)!));
            var matching = receipts.Where(item => item.Receipt.Runtime == runtime && item.Receipt.Scenario == scenario
                && item.Receipt.BundleDigest == digest).OrderByDescending(item => item.Path, StringComparer.Ordinal);
            var found = false;
            foreach (var (path, receipt) in matching)
            {
                var directory = Path.GetDirectoryName(path)!;
                try
                {
                    var files = receipt.Evidence.Select(file => (File: file, Path: ComparisonArtifacts.Resolve(directory, file.Path))).ToArray();
                    if (files.Length == 0 || files.Any(item => !File.Exists(item.Path) || ComparisonArtifacts.Hash(item.Path) != item.File.Sha256))
                        continue;
                    evidence.Add(path);
                    evidence.AddRange(files.Select(item => item.Path));
                    found = true;
                    break;
                }
                catch (Exception exception) when (exception is InvalidDataException or IOException)
                {
                }
            }
            if (!found)
                return [];
        }
        return evidence.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static IEnumerable<string> EnumerateEvidence(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var file in Directory.EnumerateFiles(directory))
                if (new FileInfo(file).LinkTarget is null)
                    yield return file;
            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                if (Path.GetFileName(child) is "checkout" or "runtime" or "xdg" or "home" or "tmp" or ".git"
                    || new DirectoryInfo(child).LinkTarget is not null)
                    continue;
                pending.Push(child);
            }
        }
    }
}
