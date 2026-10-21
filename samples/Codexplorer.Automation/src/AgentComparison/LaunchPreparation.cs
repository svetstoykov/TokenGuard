using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Codexplorer.Automation.Configuration;
using Codexplorer.Automation.AgentComparison.Recording;
using Codexplorer.Automation.Scoring;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Codexplorer.Automation.AgentComparison;

/// <summary>Creates a hash-bound launch package and fail-closed readiness result.</summary>
internal sealed class LaunchPreparation : ILaunchPreparation
{
    private const string Model = "qwen/qwen3.7-flash";
    private const string ProviderCatalogUrl = "https://openrouter.ai/api/v1/models/qwen/qwen3.7-flash/endpoints";
    private const string ProviderTag = "alibaba";
    private const string PinnedOpenCodeVersion = "1.19.3";
    private const string PinnedOpenCodeCommit = "e3c4eb242585dcd77be1a867391b7c25fd47ce52";
    private const string PinnedOpenCodeArchiveSha256 = "c2d0fecd27acf6e08be02c1f5d384614d07495587766bcc4a6bff3971749bb64";
    private const int SchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly JsonSerializerOptions CompactJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HttpClient SharedHttpClient = new();
    private static readonly IReadOnlyList<Tier> Tiers =
    [
        new("100k", "long-sessions-100k.json", 80, 8, 50_000),
        new("200k", "long-sessions-200k.json", 140, 10, 65_000),
        new("300k", "long-sessions-300k.json", 200, 12, 100_000)
    ];
    private static readonly string[] Arms = ["codexplorer-control", "codexplorer-treatment", "opencode"];

    private readonly IComparisonCredentialResolver _credentialResolver;
    private readonly HttpClient _httpClient;

    /// <summary>Initializes a launch-preparation service.</summary>
    /// <param name="credentialResolver">The secret-safe credential resolver.</param>
    /// <param name="httpClient">The client used for unauthenticated provider-catalog reads.</param>
    /// <exception cref="ArgumentNullException"><paramref name="credentialResolver" /> is <see langword="null" />.</exception>
    public LaunchPreparation(IComparisonCredentialResolver credentialResolver, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(credentialResolver);
        this._credentialResolver = credentialResolver;
        this._httpClient = httpClient ?? SharedHttpClient;
    }

    /// <inheritdoc />
    public async Task<ComparisonLaunchPlan> PrepareAsync(
        string repositoryRoot,
        string outputDirectory,
        int repetitions,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        if (repetitions <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(repetitions), "Repetitions must be positive.");
        }

        var root = Path.GetFullPath(repositoryRoot);
        var outputRoot = Path.GetFullPath(outputDirectory, root);
        var approvedOutputRoot = Path.GetFullPath(Path.Combine(root, ".artifacts", "reports", "opencode-comparison"));
        if (!IsWithin(outputRoot, approvedOutputRoot))
        {
            throw new ArgumentException("Comparison evidence must be stored under .artifacts/reports/opencode-comparison.", nameof(outputDirectory));
        }

        if (HasReparsePoint(outputRoot))
        {
            throw new ArgumentException("Comparison evidence paths must not contain symbolic links.", nameof(outputDirectory));
        }

        var scratch = Path.Combine(outputRoot, "scratch");
        var promptsRoot = Path.Combine(scratch, "prompts");
        Directory.CreateDirectory(promptsRoot);

        var blockers = new List<string>();
        var inputs = new List<FileDigest>();
        var manifests = new List<(Tier Tier, AutomationManifestSnapshot Snapshot)>();
        foreach (var tier in Tiers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifestPath = Path.Combine(root, "samples", "Codexplorer.Automation", "src", "tasks", tier.ManifestName);
            var options = new CodexplorerAutomationOptions { ManifestPath = manifestPath };
            var loader = new AutomationTaskManifestLoader(
                Options.Create(options), NullLogger<AutomationTaskManifestLoader>.Instance, new AnswerScorer());
            var snapshot = loader.LoadSnapshot();
            if (snapshot.Tasks.Count != 2 || snapshot.Tasks.Any(task => task.Probe is not null))
            {
                blockers.Add($"Manifest '{tier.ManifestName}' must contain exactly two tasks without probes.");
            }

            var relativeManifest = Path.GetRelativePath(root, manifestPath);
            inputs.Add(new FileDigest(relativeManifest, snapshot.Sha256));
            manifests.Add((tier, snapshot));
        }

        var sessions = await CreateSessionsAsync(root, promptsRoot, manifests, repetitions, inputs, cancellationToken);
        var catalogPath = Path.Combine(scratch, "provider-catalog.json");
        string? selectedProvider = null;
        try
        {
            using var response = await this._httpClient.GetAsync(ProviderCatalogUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            await using var destination = File.Create(catalogPath);
            await JsonSerializer.SerializeAsync(destination, document.RootElement, JsonOptions, cancellationToken);
            selectedProvider = SelectProvider(document.RootElement);
            if (selectedProvider is null)
            {
                blockers.Add($"The public provider catalog has no healthy '{ProviderTag}' endpoint for {Model}.");
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException
            or InvalidOperationException or FormatException)
        {
            blockers.Add($"The public provider catalog could not be resolved ({exception.GetType().Name}).");
        }

        if (File.Exists(catalogPath))
        {
            inputs.Add(await DigestFileAsync(root, catalogPath, cancellationToken));
        }

        var limits = await ReadCatalogLimitsAsync(catalogPath, cancellationToken);
        if (limits is null)
        {
            blockers.Add("The provider catalog did not expose a selected endpoint's context and completion limits.");
        }

        var settings = new ComparisonSettings(Model, selectedProvider, 32_768, ReasoningEnabled: true)
        {
            ProviderContextTokens = limits?.ContextWindowTokens,
            ProviderMaxCompletionTokens = limits?.MaxCompletionTokens
        };
        if (settings.ProviderContextTokens is not > 32_768 || settings.ProviderMaxCompletionTokens is not >= 32_768)
            blockers.Add("The selected endpoint cannot support the shared output ceiling and context policy.");
        var settingsPath = Path.Combine(scratch, "effective-settings.json");
        await File.WriteAllBytesAsync(settingsPath, JsonSerializer.SerializeToUtf8Bytes(new
        {
            model = Model,
            provider = selectedProvider,
            providerFallbacks = false,
            maxOutputTokens = 32_768,
            reasoningEnabled = true,
            samplingParameters = (string?)null,
            openCode = new
            {
                mainModel = Model,
                compactionAgentModel = Model,
                smallModel = Model,
                contextWindowTokens = limits?.ContextWindowTokens,
                providerMaxCompletionTokens = limits?.MaxCompletionTokens,
                compaction = new
                {
                    automatic = true,
                    pruning = false,
                    reservedTokens = 20_000,
                    preserveRecentTokens = 15_000,
                    derivedFromPinnedDefaults = true,
                    bufferDefaultsSource = "https://github.com/anomalyco/opencode/blob/v1.19.3/packages/opencode/src/session/overflow.ts",
                    preservedTailDefaultsSource = "https://github.com/anomalyco/opencode/blob/v1.19.3/packages/opencode/src/session/compaction.ts"
                }
            }
        }, CompactJsonOptions), cancellationToken);
        inputs.Add(await DigestFileAsync(root, settingsPath, cancellationToken));

        var runtimePinPath = Path.Combine(scratch, "opencode-runtime-pin.json");
        await File.WriteAllBytesAsync(runtimePinPath, JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = PinnedOpenCodeVersion,
            tagCommit = PinnedOpenCodeCommit,
            officialReleaseArchive = "opencode-darwin-arm64.zip",
            officialReleaseArchiveSha256 = PinnedOpenCodeArchiveSha256,
            binaryPath = Path.Combine(scratch, "tools", "opencode-v1.19.3", "opencode")
        }, CompactJsonOptions), cancellationToken);
        inputs.Add(await DigestFileAsync(root, runtimePinPath, cancellationToken));

        var bundles = await PrepareRuntimeBundlesAsync(root, scratch, inputs, blockers, cancellationToken);
        await AddOfflineVerificationEvidenceAsync(root, scratch, bundles, inputs, blockers, cancellationToken);
        var baseSha = await GitAsync(root, ["rev-parse", "main"], cancellationToken);

        var sourceSha = await GitAsync(root, ["rev-parse", "HEAD"], cancellationToken);
        var dirtyDigest = await ComputeDirtyDigestAsync(root, cancellationToken);
        var sourceDirty = !string.IsNullOrWhiteSpace(await GitAsync(root, ["status", "--porcelain", "--untracked-files=all"], cancellationToken));
        bundles = bundles.Select(bundle => bundle with
        {
            SourceSha = bundle.Name == "opencode" ? PinnedOpenCodeCommit : sourceSha,
            Dirty = bundle.Name != "opencode" && sourceDirty
        }).ToArray();
        var plan = new ComparisonLaunchPlan(
            SchemaVersion,
            $"opencode-comparison-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssZ}-{Guid.NewGuid():N}",
            root,
            "main",
            baseSha,
            sourceSha,
            dirtyDigest,
            DateTimeOffset.UtcNow,
            Path.GetRelativePath(root, outputRoot),
            settings,
            bundles,
            inputs,
            sessions,
            blockers.AsReadOnly(),
            string.Empty,
            LiveExecutionStarted: false)
        {
            SourceDirty = sourceDirty, BaseDirty = false
        };
        plan = plan with { PlanDigest = ComparisonPlanFile.ComputeDigest(plan) };

        var planPath = Path.Combine(scratch, "launch-plan.json");
        await File.WriteAllBytesAsync(planPath, JsonSerializer.SerializeToUtf8Bytes(plan, JsonOptions), cancellationToken);
        return plan;
    }

    /// <inheritdoc />
    public async Task<ComparisonReadiness> PreflightAsync(string planPath, CancellationToken cancellationToken)
    {
        var fullPlanPath = Path.GetFullPath(planPath);
        var planRoot = FindRepositoryRootForPlan(fullPlanPath);
        var approvedPlanDirectory = Path.Combine(planRoot, ".artifacts", "reports", "opencode-comparison", "scratch");
        if (!IsWithin(fullPlanPath, approvedPlanDirectory) || HasReparsePoint(fullPlanPath))
        {
            throw new InvalidDataException("The launch plan must be a regular file under the authorized comparison scratch directory.");
        }

        ComparisonLaunchPlan plan;
        try
        {
            plan = await ComparisonPlanFile.LoadAsync(fullPlanPath, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException or IOException or UnauthorizedAccessException)
        {
            var rejected = new ComparisonReadiness(SchemaVersion, string.Empty, false, false, false, "unavailable",
                DateTimeOffset.UtcNow, [$"The launch plan could not be validated ({exception.Message})"]);
            await WriteReadinessAsync(fullPlanPath, rejected, cancellationToken);
            return rejected;
        }

        if (!PathEquals(plan.RepositoryRoot, planRoot))
        {
            var rejected = new ComparisonReadiness(SchemaVersion, plan.PlanDigest, false, false, false, "unavailable",
                DateTimeOffset.UtcNow, ["The plan repository root does not match its containing worktree."]);
            await WriteReadinessAsync(fullPlanPath, rejected, cancellationToken);
            return rejected;
        }

        var blockers = new List<string>(plan.Blockers);
        if (plan.Isolation != ComparisonIsolationPlan.Default)
            blockers.Add("The plan changes the compiled per-session isolation layout.");
        var sourceDirty = !string.IsNullOrWhiteSpace(await GitAsync(
            plan.RepositoryRoot, ["status", "--porcelain", "--untracked-files=all"], cancellationToken));
        if (plan.BaseDirty || plan.SourceDirty != sourceDirty)
            blockers.Add("The source or base dirty flags differ from the prepared provenance.");
        if (plan.Bundles.Any(bundle => bundle.SourceSha != (bundle.Name == "opencode" ? PinnedOpenCodeCommit : plan.SourceSha)
            || bundle.Dirty != (bundle.Name != "opencode" && plan.SourceDirty)))
            blockers.Add("The runtime source revisions or dirty flags differ from the prepared source provenance.");

        if (plan.LiveExecutionStarted)
        {
            blockers.Add("The plan indicates that live execution has started.");
        }

        if (plan.Settings.Model != Model || plan.Settings.MaxOutputTokens != 32_768 || !plan.Settings.ReasoningEnabled)
        {
            blockers.Add("The plan does not enforce the shared model, 32,768-token output cap and reasoning setting.");
        }
        if (plan.Settings.ProviderContextTokens is not > 32_768 || plan.Settings.ProviderMaxCompletionTokens is not >= 32_768)
            blockers.Add("The plan has no compatible selected endpoint context/completion limits.");

        ValidateExpectedMatrix(plan, blockers, cancellationToken);

        if (plan.SchemaVersion != SchemaVersion
            || !string.Equals(plan.BaseRef, "main", StringComparison.Ordinal)
            || !string.Equals(plan.OutputRoot, Path.Combine(".artifacts", "reports", "opencode-comparison"), StringComparison.Ordinal))
        {
            blockers.Add("The plan does not match the compiled comparison contract schema, main base, or output root.");
        }

        var currentBase = await GitAsync(plan.RepositoryRoot, ["rev-parse", "main"], cancellationToken);
        var currentSource = await GitAsync(plan.RepositoryRoot, ["rev-parse", "HEAD"], cancellationToken);
        var currentDirty = await ComputeDirtyDigestAsync(plan.RepositoryRoot, cancellationToken);
        if (!string.Equals(currentBase, plan.BaseSha, StringComparison.OrdinalIgnoreCase))
        {
            blockers.Add("The comparison base SHA changed after preparation.");
        }

        if (!IsFullSha(plan.BaseSha))
        {
            blockers.Add("The plan does not use the resolved main baseline SHA.");
        }

        try
        {
            if (IsFullSha(plan.BaseSha) && IsFullSha(plan.SourceSha))
            {
                await GitAsync(plan.RepositoryRoot, ["merge-base", "--is-ancestor", plan.BaseSha, plan.SourceSha], cancellationToken);
            }
        }
        catch (InvalidOperationException)
        {
            blockers.Add("The recorded main baseline is not an ancestor of the source revision.");
        }

        if (!string.Equals(currentSource, plan.SourceSha, StringComparison.OrdinalIgnoreCase))
        {
            blockers.Add("The source SHA changed after preparation.");
        }

        if (!string.Equals(currentDirty, plan.SourceDirtyDigest, StringComparison.OrdinalIgnoreCase))
        {
            blockers.Add("The source dirty-state digest changed after preparation.");
        }

        foreach (var input in plan.Inputs)
        {
            string path;
            try
            {
                path = ResolveInputPath(plan.RepositoryRoot, input.Path);
            }
            catch (InvalidDataException)
            {
                blockers.Add($"Prepared input escapes its authorized root or uses a symbolic link: {input.Path}");
                continue;
            }

            if (!File.Exists(path) || !string.Equals(await HashFileAsync(path, cancellationToken), input.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                blockers.Add($"Prepared input changed or is missing: {input.Path}");
            }
        }

        var runtimePin = plan.Inputs.FirstOrDefault(input => input.Path.EndsWith("opencode-runtime-pin.json", StringComparison.Ordinal));
        if (runtimePin is null)
        {
            blockers.Add("The plan has no pinned OpenCode release evidence.");
        }
        else
        {
            var runtimePinPath = ResolveInputPath(plan.RepositoryRoot, runtimePin.Path);
            try
            {
                await using var stream = File.OpenRead(runtimePinPath);
                using var pinDocument = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                var pin = pinDocument.RootElement;
                if (pin.GetProperty("version").GetString() != PinnedOpenCodeVersion
                    || pin.GetProperty("tagCommit").GetString() != PinnedOpenCodeCommit
                    || pin.GetProperty("officialReleaseArchiveSha256").GetString()
                        != PinnedOpenCodeArchiveSha256)
                {
                    blockers.Add("Pinned OpenCode release evidence does not match the approved tag and archive.");
                }
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or IOException)
            {
                blockers.Add("Pinned OpenCode release evidence is malformed.");
            }
        }

        var releaseArchive = plan.Inputs.FirstOrDefault(input => input.Path.EndsWith("opencode-v1.19.3-release.zip", StringComparison.Ordinal));
        if (releaseArchive is null
            || !string.Equals(releaseArchive.Sha256, PinnedOpenCodeArchiveSha256, StringComparison.OrdinalIgnoreCase))
        {
            blockers.Add("The official OpenCode v1.19.3 release archive digest is absent or unexpected.");
        }

        await ValidateOfflineVerificationAsync(plan, blockers, cancellationToken);

        var expectedBundleNames = new HashSet<string>(["opencode", "codexplorer", "automation"], StringComparer.Ordinal);
        if (!plan.Bundles.Select(bundle => bundle.Name).ToHashSet(StringComparer.Ordinal).SetEquals(expectedBundleNames))
        {
            blockers.Add("The plan must inventory the OpenCode, Codexplorer and automation runtime bundles.");
        }

        foreach (var bundle in plan.Bundles)
        {
            if (bundle.Name is not ("opencode" or "codexplorer" or "automation"))
            {
                blockers.Add($"Unexpected runtime bundle '{bundle.Name}'.");
                continue;
            }

            if (bundle.Files.Count == 0 || !bundle.Files.Any(file => Path.GetFullPath(file.Path) == Path.GetFullPath(bundle.Executable)))
            {
                blockers.Add($"Runtime bundle '{bundle.Name}' has no complete executable inventory.");
            }

            var toolsRoot = Path.Combine(plan.RepositoryRoot, plan.OutputRoot, "scratch", "tools");
            if (!IsWithin(bundle.Executable, toolsRoot)
                || ComparisonBundleInventory.HasExcludedEntries(Path.GetDirectoryName(bundle.Executable)!))
            {
                blockers.Add($"Runtime bundle '{bundle.Name}' is outside retained tools or contains filtered files or links.");
            }

            var expectedExecutable = bundle.Name switch
            {
                "opencode" => "opencode",
                "codexplorer" => OperatingSystem.IsWindows() ? "Codexplorer.exe" : "Codexplorer",
                "automation" => OperatingSystem.IsWindows() ? "Codexplorer.Automation.exe" : "Codexplorer.Automation",
                _ => string.Empty
            };
            if (!string.Equals(Path.GetFileName(bundle.Executable), expectedExecutable, StringComparison.Ordinal))
            {
                blockers.Add($"Runtime bundle '{bundle.Name}' does not name its expected executable.");
            }

            foreach (var file in bundle.Files)
            {
                if (!IsWithin(file.Path, Path.GetDirectoryName(bundle.Executable)!) || HasReparsePoint(file.Path))
                {
                    blockers.Add($"Runtime bundle path escapes its bundle or uses a symbolic link: {file.Path}");
                    continue;
                }

            if (!File.Exists(file.Path)
                || !string.Equals(
                    await HashFileAsync(file.Path, cancellationToken),
                    file.Sha256,
                    StringComparison.OrdinalIgnoreCase))
                {
                    blockers.Add($"Runtime bundle changed or is missing: {file.Path}");
                }
            }

            var observedFiles = await InventoryDirectoryAsync(Path.GetDirectoryName(bundle.Executable)!, cancellationToken);
            if (!observedFiles.Select(file => file.Path).ToHashSet(StringComparer.Ordinal)
                .SetEquals(bundle.Files.Select(file => file.Path)))
            {
                blockers.Add($"Runtime bundle '{bundle.Name}' inventory has missing or added files.");
            }

            if (bundle.Name == "opencode")
            {
                if (File.Exists(bundle.Executable))
                {
                    var version = await ProcessAsync(bundle.Executable, ["--version"], plan.RepositoryRoot, cancellationToken);
                    if (!string.Equals(version.Trim(), PinnedOpenCodeVersion, StringComparison.Ordinal))
                    {
                        blockers.Add("The retained OpenCode binary does not report the pinned v1.19.3 version.");
                    }
                }
            }
        }

        var credential = this._credentialResolver.Inspect();
        if (!credential.Present)
        {
            blockers.Add("No existing OpenRouter credential is present in the supported environment or ignored development configuration.");
        }

        if (plan.Settings.Provider is null)
        {
            blockers.Add("The plan has no selected provider route.");
        }
        else
        {
            var catalogInput = plan.Inputs.FirstOrDefault(input => input.Path.EndsWith("provider-catalog.json", StringComparison.Ordinal));
            if (catalogInput is null)
            {
                blockers.Add("The plan has no provider-catalog snapshot.");
            }
            else
            {
                var catalogPath = ResolveInputPath(plan.RepositoryRoot, catalogInput.Path);
                if (!File.Exists(catalogPath))
                {
                    blockers.Add("The provider-catalog snapshot is missing.");
                }
                else
                {
                    try
                    {
                        await using var catalogStream = File.OpenRead(catalogPath);
                        using var catalogDocument = await JsonDocument.ParseAsync(catalogStream, cancellationToken: cancellationToken);
                        if (!string.Equals(SelectProvider(catalogDocument.RootElement), plan.Settings.Provider, StringComparison.Ordinal))
                        {
                            blockers.Add("The selected provider route does not match the catalog snapshot.");
                        }
                    }
                    catch (JsonException)
                    {
                        blockers.Add("The provider-catalog snapshot is not valid JSON.");
                    }
                }
            }
        }

        await ValidateEffectiveSettingsAsync(plan, blockers, cancellationToken);

        var outputRoot = Path.GetFullPath(plan.OutputRoot, plan.RepositoryRoot);
        if (HasReparsePoint(outputRoot) || !await IsWritableDirectoryAsync(outputRoot, cancellationToken))
        {
            blockers.Add("The comparison evidence directory is not writable or contains a symbolic link.");
        }

        var readiness = new ComparisonReadiness(
            SchemaVersion,
            plan.PlanDigest,
            blockers.Count == 0,
            LiveExecutionStarted: false,
            credential.Present,
            credential.Source,
            DateTimeOffset.UtcNow,
            blockers.Distinct(StringComparer.Ordinal).ToArray());
        await WriteReadinessAsync(fullPlanPath, readiness, cancellationToken);
        return readiness;
    }

    private static void ValidateExpectedMatrix(
        ComparisonLaunchPlan plan,
        List<string> blockers,
        CancellationToken cancellationToken)
    {
        var repetitions = plan.Sessions.Count == 0 ? 0 : plan.Sessions.Max(session => session.Repetition);
        if (repetitions <= 0 || plan.Sessions.Count != 6 * Arms.Length * repetitions)
        {
            blockers.Add("The launch plan does not contain the compiled six-task, three-arm matrix.");
            return;
        }

        try
        {
            var manifests = new List<(Tier Tier, AutomationManifestSnapshot Snapshot)>();
            foreach (var tier in Tiers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var manifestPath = Path.Combine(
                    plan.RepositoryRoot,
                    "samples",
                    "Codexplorer.Automation",
                    "src",
                    "tasks",
                    tier.ManifestName);
                var options = new CodexplorerAutomationOptions { ManifestPath = manifestPath };
                var loader = new AutomationTaskManifestLoader(
                    Options.Create(options),
                    NullLogger<AutomationTaskManifestLoader>.Instance,
                    new AnswerScorer());
                var snapshot = loader.LoadSnapshot();
                if (snapshot.Tasks.Count != 2 || snapshot.Tasks.Any(task => task.Probe is not null))
                {
                    blockers.Add($"Manifest '{tier.ManifestName}' no longer matches the compiled comparison corpus.");
                    return;
                }

                var relativePath = Path.GetRelativePath(plan.RepositoryRoot, manifestPath);
                var boundInput = plan.Inputs.FirstOrDefault(input =>
                    string.Equals(input.Path, relativePath, StringComparison.Ordinal));
                if (boundInput is null || !string.Equals(boundInput.Sha256, snapshot.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    blockers.Add($"The launch plan is not bound to the current source manifest '{tier.ManifestName}'.");
                    return;
                }

                manifests.Add((tier, snapshot));
            }

            var expected = BuildExpectedSessions(manifests, repetitions);
            if (expected.Count != plan.Sessions.Count)
            {
                blockers.Add("The launch plan does not contain the compiled comparison matrix size.");
                return;
            }

            for (var index = 0; index < expected.Count; index++)
            {
                if (!SessionsEqual(expected[index], plan.Sessions[index]))
                {
                    blockers.Add("The launch plan changes a frozen task, prompt, arm order, tier budget, or context window.");
                    return;
                }
            }

            foreach (var (tier, snapshot) in manifests)
            {
                foreach (var task in snapshot.Tasks)
                {
                    var sourcePrompt = task.InitialPrompt!;
                    var adaptedPrompt = AdaptPrompt(sourcePrompt);
                    for (var repetition = 1; repetition <= repetitions; repetition++)
                    {
                        var stem = $"{task.TaskId}.rep-{repetition}";
                        var expectedFiles = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            [$"{stem}.source.md"] = Sha256(sourcePrompt),
                            [$"{stem}.opencode.md"] = Sha256(adaptedPrompt),
                            [$"{stem}.diff.md"] = Sha256(MakePromptDiff(sourcePrompt, adaptedPrompt))
                        };
                        foreach (var (name, digest) in expectedFiles)
                        {
                            var relativePath = Path.Combine(plan.OutputRoot, "scratch", "prompts", name);
                            var input = plan.Inputs.FirstOrDefault(value =>
                                string.Equals(value.Path, relativePath, StringComparison.Ordinal));
                            if (input is null || !string.Equals(input.Sha256, digest, StringComparison.OrdinalIgnoreCase))
                            {
                                blockers.Add($"The plan is missing expected original or adapted prompt evidence for {tier.Name}/{task.TaskId}.");
                                return;
                            }
                        }
                    }
                }
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or JsonException
            or ArgumentException or InvalidOperationException)
        {
            blockers.Add($"The compiled source corpus could not be revalidated ({exception.GetType().Name}).");
        }
    }

    private static IReadOnlyList<ComparisonSessionPlan> BuildExpectedSessions(
        IReadOnlyList<(Tier Tier, AutomationManifestSnapshot Snapshot)> manifests,
        int repetitions)
    {
        var sessions = new List<ComparisonSessionPlan>(6 * Arms.Length * repetitions);
        for (var repetition = 1; repetition <= repetitions; repetition++)
        {
            var taskIndex = 0;
            foreach (var (tier, snapshot) in manifests)
            {
                foreach (var task in snapshot.Tasks)
                {
                    var sourcePrompt = task.InitialPrompt
                        ?? throw new InvalidDataException($"Task '{task.TaskId}' has no source prompt.");
                    var adaptedPrompt = AdaptPrompt(sourcePrompt);
                    var startingArm = (repetition - 1 + taskIndex) % Arms.Length;
                    for (var armOffset = 0; armOffset < Arms.Length; armOffset++)
                    {
                        var arm = Arms[(startingArm + armOffset) % Arms.Length];
                        var prompt = arm == "opencode" ? adaptedPrompt : sourcePrompt;
                        sessions.Add(new ComparisonSessionPlan(
                            $"{tier.Name}-{task.TaskId}-r{repetition}-{arm}",
                            tier.Name,
                            repetition,
                            arm,
                            task,
                            prompt,
                            Sha256(sourcePrompt),
                            Sha256(prompt),
                            tier.MaxAgentCalls,
                            tier.WrapUpCalls,
                            arm == "codexplorer-control"
                                ? 1_000_000
                                : arm == "codexplorer-treatment" ? tier.TreatmentWindow : null));
                    }

                    taskIndex++;
                }
            }
        }

        return sessions.AsReadOnly();
    }

    /// <summary>Compares every source task, check, prompt and allowance in two session contracts.</summary>
    /// <param name="expected">The session reconstructed from the frozen corpus.</param>
    /// <param name="actual">The session supplied in the plan.</param>
    /// <returns>Whether the complete executable task contracts match.</returns>
    internal static bool SessionsEqual(ComparisonSessionPlan expected, ComparisonSessionPlan actual)
    {
        if (expected.Id != actual.Id
            || expected.Tier != actual.Tier
            || expected.Repetition != actual.Repetition
            || expected.Arm != actual.Arm
            || expected.Prompt != actual.Prompt
            || expected.SourcePromptSha256 != actual.SourcePromptSha256
            || expected.AdaptedPromptSha256 != actual.AdaptedPromptSha256
            || expected.MaxAgentCalls != actual.MaxAgentCalls
            || expected.WrapUpCalls != actual.WrapUpCalls
            || expected.ContextWindowTokens != actual.ContextWindowTokens)
        {
            return false;
        }

        var expectedTask = JsonSerializer.SerializeToUtf8Bytes(expected.Task, CompactJsonOptions);
        var actualTask = JsonSerializer.SerializeToUtf8Bytes(actual.Task, CompactJsonOptions);
        return expectedTask.AsSpan().SequenceEqual(actualTask);
    }

    private static bool IsFullSha(string value) => value.Length == 40 && value.All(Uri.IsHexDigit);

    private static bool PathEquals(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string FindRepositoryRootForPlan(string planPath)
    {
        for (var current = new DirectoryInfo(Path.GetDirectoryName(planPath)!); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "TokenGuard.sln")))
            {
                return current.FullName;
            }
        }

        throw new InvalidDataException("The launch plan is outside a TokenGuard repository.");
    }

    private static async Task WriteReadinessAsync(string planPath, ComparisonReadiness readiness, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(planPath))!;
        Directory.CreateDirectory(directory);
        var readinessPath = Path.Combine(directory, "readiness.json");
        var readinessBytes = JsonSerializer.SerializeToUtf8Bytes(readiness, JsonOptions);
        await File.WriteAllBytesAsync(readinessPath, readinessBytes, cancellationToken);
    }

    private static string? SelectProvider(JsonElement catalog)
    {
        if (!catalog.TryGetProperty("data", out var data)
            || !data.TryGetProperty("endpoints", out var endpoints)
            || endpoints.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return endpoints.EnumerateArray().Any(endpoint =>
            endpoint.TryGetProperty("tag", out var tag)
            && tag.GetString() == ProviderTag
            && endpoint.TryGetProperty("status", out var status)
            && status.GetInt32() == 0)
            ? ProviderTag
            : null;
    }

    private static async Task<CatalogLimits?> ReadCatalogLimitsAsync(string catalogPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(catalogPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(catalogPath);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty("endpoints", out var endpoints)
            || endpoints.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var endpoint in endpoints.EnumerateArray())
        {
            if (!endpoint.TryGetProperty("tag", out var tag) || tag.GetString() != ProviderTag
                || !endpoint.TryGetProperty("status", out var status) || status.GetInt32() != 0)
            {
                continue;
            }

            if (endpoint.TryGetProperty("context_length", out var context) && context.ValueKind == JsonValueKind.Number
                && endpoint.TryGetProperty("max_completion_tokens", out var completion) && completion.ValueKind == JsonValueKind.Number
                && context.TryGetInt32(out var contextTokens) && completion.TryGetInt32(out var completionTokens))
            {
                return new CatalogLimits(contextTokens, completionTokens);
            }
        }

        return null;
    }

    private static async Task ValidateEffectiveSettingsAsync(
        ComparisonLaunchPlan plan,
        List<string> blockers,
        CancellationToken cancellationToken)
    {
        var settingsInput = plan.Inputs.FirstOrDefault(input =>
            input.Path.EndsWith("effective-settings.json", StringComparison.Ordinal));
        var catalogInput = plan.Inputs.FirstOrDefault(input =>
            input.Path.EndsWith("provider-catalog.json", StringComparison.Ordinal));
        if (settingsInput is null || catalogInput is null)
        {
            blockers.Add("The plan is missing effective-settings or provider-catalog evidence.");
            return;
        }

        try
        {
            var settingsPath = ResolveInputPath(plan.RepositoryRoot, settingsInput.Path);
            var catalogPath = ResolveInputPath(plan.RepositoryRoot, catalogInput.Path);
            await using var settingsStream = File.OpenRead(settingsPath);
            using var settingsDocument = await JsonDocument.ParseAsync(settingsStream, cancellationToken: cancellationToken);
            var settings = settingsDocument.RootElement;
            var openCode = settings.GetProperty("openCode");
            var compaction = openCode.GetProperty("compaction");
            var limits = await ReadCatalogLimitsAsync(catalogPath, cancellationToken);
            var valid = settings.GetProperty("model").GetString() == Model
                && settings.GetProperty("provider").GetString() == plan.Settings.Provider
                && settings.GetProperty("providerFallbacks").ValueKind == JsonValueKind.False
                && settings.GetProperty("maxOutputTokens").GetInt32() == 32_768
                && settings.GetProperty("reasoningEnabled").ValueKind == JsonValueKind.True
                && settings.GetProperty("samplingParameters").ValueKind == JsonValueKind.Null
                && openCode.GetProperty("mainModel").GetString() == Model
                && openCode.GetProperty("compactionAgentModel").GetString() == Model
                && openCode.GetProperty("smallModel").GetString() == Model
                && openCode.GetProperty("contextWindowTokens").GetInt32() == limits?.ContextWindowTokens
                && plan.Settings.ProviderContextTokens == limits?.ContextWindowTokens
                && plan.Settings.ProviderMaxCompletionTokens == limits?.MaxCompletionTokens
                && openCode.GetProperty("providerMaxCompletionTokens").GetInt32() == limits?.MaxCompletionTokens
                && compaction.GetProperty("automatic").ValueKind == JsonValueKind.True
                && compaction.GetProperty("pruning").ValueKind == JsonValueKind.False
                && compaction.GetProperty("reservedTokens").GetInt32() == 20_000
                && compaction.GetProperty("preserveRecentTokens").GetInt32() == 15_000
                && compaction.GetProperty("derivedFromPinnedDefaults").ValueKind == JsonValueKind.True
                && compaction.GetProperty("bufferDefaultsSource").GetString()
                    == "https://github.com/anomalyco/opencode/blob/v1.19.3/packages/opencode/src/session/overflow.ts"
                && compaction.GetProperty("preservedTailDefaultsSource").GetString()
                    == "https://github.com/anomalyco/opencode/blob/v1.19.3/packages/opencode/src/session/compaction.ts";
            if (!valid)
            {
                blockers.Add("The effective settings evidence changes the compiled model, routing or OpenCode context policy.");
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or IOException
            or KeyNotFoundException or InvalidDataException)
        {
            blockers.Add($"The effective settings evidence could not be validated ({exception.GetType().Name}).");
        }
    }

    /// <summary>Creates the ordered session matrix and writes each prompt variant and diff.</summary>
    /// <param name="repositoryRoot">The source repository root.</param>
    /// <param name="promptsRoot">The prompt evidence directory.</param>
    /// <param name="manifests">The validated tier manifests.</param>
    /// <param name="repetitions">The positive repetition count.</param>
    /// <param name="inputs">The destination list for prompt file digests.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task containing the ordered session matrix.</returns>
    internal static async Task<IReadOnlyList<ComparisonSessionPlan>> CreateSessionsAsync(
        string repositoryRoot,
        string promptsRoot,
        IReadOnlyList<(Tier Tier, AutomationManifestSnapshot Snapshot)> manifests,
        int repetitions,
        List<FileDigest> inputs,
        CancellationToken cancellationToken)
    {
        var sessions = new List<ComparisonSessionPlan>(checked(manifests.Sum(item => item.Snapshot.Tasks.Count) * Arms.Length * repetitions));
        for (var repetition = 1; repetition <= repetitions; repetition++)
        {
            var taskIndex = 0;
            foreach (var (tier, snapshot) in manifests)
            {
                foreach (var task in snapshot.Tasks)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var sourcePrompt = task.InitialPrompt ?? throw new InvalidDataException($"Task '{task.TaskId}' has no prompt.");
                    var adaptedPrompt = AdaptPrompt(sourcePrompt);
                    var promptStem = $"{task.TaskId}.rep-{repetition}";
                    var originalPath = Path.Combine(promptsRoot, $"{promptStem}.source.md");
                    var adaptedPath = Path.Combine(promptsRoot, $"{promptStem}.opencode.md");
                    var diffPath = Path.Combine(promptsRoot, $"{promptStem}.diff.md");
                    await File.WriteAllTextAsync(originalPath, sourcePrompt, cancellationToken);
                    await File.WriteAllTextAsync(adaptedPath, adaptedPrompt, cancellationToken);
                    await File.WriteAllTextAsync(diffPath, MakePromptDiff(sourcePrompt, adaptedPrompt), cancellationToken);
                    inputs.Add(await DigestFileAsync(repositoryRoot, originalPath, cancellationToken));
                    inputs.Add(await DigestFileAsync(repositoryRoot, adaptedPath, cancellationToken));
                    inputs.Add(await DigestFileAsync(repositoryRoot, diffPath, cancellationToken));

                    var startingArm = (repetition - 1 + taskIndex) % Arms.Length;
                    for (var armOffset = 0; armOffset < Arms.Length; armOffset++)
                    {
                        var arm = Arms[(startingArm + armOffset) % Arms.Length];
                        var prompt = arm == "opencode" ? adaptedPrompt : sourcePrompt;
                        sessions.Add(new ComparisonSessionPlan(
                            $"{tier.Name}-{task.TaskId}-r{repetition}-{arm}",
                            tier.Name,
                            repetition,
                            arm,
                            task,
                            prompt,
                            Sha256(sourcePrompt),
                            Sha256(prompt),
                            tier.MaxAgentCalls,
                            tier.WrapUpCalls,
                            arm == "codexplorer-control" ? 1_000_000 : arm == "codexplorer-treatment" ? tier.TreatmentWindow : null));
                    }

                    taskIndex++;
                }
            }
        }

        return sessions.AsReadOnly();
    }

    /// <summary>Adapts artifact destinations and tool terminology for OpenCode.</summary>
    /// <param name="source">The original Codexplorer prompt.</param>
    /// <returns>The prompt with external evidence paths and OpenCode tool names.</returns>
    internal static string AdaptPrompt(string source)
    {
        var adapted = source.Replace("evidence.md", "${ARTIFACTS_DIR}/evidence.md", StringComparison.Ordinal);
        adapted = Regex.Replace(
            adapted,
            @"\bwith the edit tool\b",
            "with OpenCode's edit tool",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        adapted = Regex.Replace(
            adapted,
            @"\busing the artifact tools\b",
            "using OpenCode's file tools",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        adapted = Regex.Replace(
            adapted,
            @"\bwith the artifact tools\b",
            "with OpenCode's file tools",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        adapted = Regex.Replace(
            adapted,
            @"\buse the artifact tools\b",
            "use OpenCode's file tools",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        adapted = Regex.Replace(
            adapted,
            @"\busing only repository tools\b",
            "using only OpenCode's repository read, search, and edit tools",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return adapted;
    }

    private static string MakePromptDiff(string source, string adapted)
    {
        var sourceLines = source.Split('\n');
        var adaptedLines = adapted.Split('\n');
        var output = new StringBuilder("--- source\n+++ OpenCode adaptation\n");
        var max = Math.Max(sourceLines.Length, adaptedLines.Length);
        for (var index = 0; index < max; index++)
        {
            var original = index < sourceLines.Length ? sourceLines[index] : null;
            var changed = index < adaptedLines.Length ? adaptedLines[index] : null;
            if (string.Equals(original, changed, StringComparison.Ordinal))
            {
                continue;
            }

            if (original is not null)
            {
                output.Append('-').AppendLine(original);
            }

            if (changed is not null)
            {
                output.Append('+').AppendLine(changed);
            }
        }

        return output.ToString();
    }

    private static async Task<IReadOnlyList<RuntimeBundle>> PrepareRuntimeBundlesAsync(
        string root,
        string scratch,
        List<FileDigest> inputs,
        List<string> blockers,
        CancellationToken cancellationToken)
    {
        var bundles = new List<RuntimeBundle>();
        var openCodeExecutable = Path.Combine(scratch, "tools", "opencode-v1.19.3", "opencode");
        if (File.Exists(openCodeExecutable))
        {
            var version = await ProcessAsync(openCodeExecutable, ["--version"], scratch, cancellationToken);
            if (!string.Equals(version.Trim(), PinnedOpenCodeVersion, StringComparison.Ordinal))
            {
                blockers.Add($"Pinned OpenCode expected {PinnedOpenCodeVersion}, but its binary reported '{version.Trim()}'.");
            }

            var releaseZip = Path.Combine(scratch, "tools", "opencode-v1.19.3-release.zip");
            var zipDigest = File.Exists(releaseZip) ? await HashFileAsync(releaseZip, cancellationToken) : string.Empty;
            if (!string.Equals(zipDigest, PinnedOpenCodeArchiveSha256, StringComparison.OrdinalIgnoreCase))
            {
                blockers.Add("The retained OpenCode binary is missing its official release-archive digest evidence.");
            }
            else
            {
                inputs.Add(await DigestFileAsync(root, releaseZip, cancellationToken));
            }

            var releaseMetadata = Path.Combine(scratch, "tools", "opencode-v1.19.3-release.json");
            if (File.Exists(releaseMetadata))
            {
                inputs.Add(await DigestFileAsync(root, releaseMetadata, cancellationToken));
            }
            else
            {
                blockers.Add("The official OpenCode release metadata snapshot is missing.");
            }

            var inventory = await InventoryDirectoryAsync(Path.GetDirectoryName(openCodeExecutable)!, cancellationToken);
            bundles.Add(new RuntimeBundle("opencode", openCodeExecutable, version.Trim(), inventory));
        }
        else
        {
            blockers.Add("The pinned OpenCode v1.19.3 executable has not been installed.");
        }

        var codexplorerSource = Path.Combine(root, "samples", "Codexplorer", "src", "bin", "Release", "net10.0");
        var bundleSuffix = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ", System.Globalization.CultureInfo.InvariantCulture);
        var codexplorerDestination = Path.Combine(scratch, "tools", $"codexplorer-release-{bundleSuffix}");
        var codexplorerExecutableName = OperatingSystem.IsWindows() ? "Codexplorer.exe" : "Codexplorer";
        var codexplorerSourceExecutable = Path.Combine(codexplorerSource, codexplorerExecutableName);
        if (File.Exists(codexplorerSourceExecutable))
        {
            CopyRuntimeDirectory(codexplorerSource, codexplorerDestination);
            var executable = Path.Combine(codexplorerDestination, codexplorerExecutableName);
            var inventory = await InventoryDirectoryAsync(codexplorerDestination, cancellationToken);
            bundles.Add(new RuntimeBundle("codexplorer", executable, "Release app bundle", inventory));
        }
        else
        {
            blockers.Add("The Codexplorer Release app host is missing; build it before preparing the retained runtime bundle.");
        }

        var automationSource = Path.Combine(root, "samples", "Codexplorer.Automation", "src", "bin", "Release", "net10.0");
        var automationExecutableName = OperatingSystem.IsWindows() ? "Codexplorer.Automation.exe" : "Codexplorer.Automation";
        var automationSourceExecutable = Path.Combine(automationSource, automationExecutableName);
        if (File.Exists(automationSourceExecutable))
        {
            var automationDestination = Path.Combine(scratch, "tools", $"automation-release-{bundleSuffix}");
            CopyRuntimeDirectory(automationSource, automationDestination);
            var executable = Path.Combine(automationDestination, automationExecutableName);
            var inventory = await InventoryDirectoryAsync(automationDestination, cancellationToken);
            bundles.Add(new RuntimeBundle("automation", executable, "Release app bundle", inventory));
        }
        else
        {
            blockers.Add("The Codexplorer.Automation Release app host is missing; build it before preparing the retained automation bundle.");
        }

        return bundles.AsReadOnly();
    }

    private static async Task AddOfflineVerificationEvidenceAsync(
        string root,
        string scratch,
        IReadOnlyList<RuntimeBundle> bundles,
        List<FileDigest> inputs,
        List<string> blockers,
        CancellationToken cancellationToken)
    {
        var markerPath = Path.Combine(scratch, "offline-verification.json");
        if (!File.Exists(markerPath))
        {
            blockers.Add("Offline verification evidence is missing; run synthetic verification for all three arms before preparing launch.");
            return;
        }

        inputs.Add(await DigestFileAsync(root, markerPath, cancellationToken));
        try
        {
            await using var stream = File.OpenRead(markerPath);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var marker = document.RootElement;
            ValidateOfflineMarkerFields(marker, blockers);
            var evidencePaths = marker.GetProperty("evidencePaths");
            if (evidencePaths.ValueKind != JsonValueKind.Array)
            {
                blockers.Add("Offline verification evidencePaths must be an array.");
                return;
            }

            var paths = new List<string>();
            foreach (var item in evidencePaths.EnumerateArray())
            {
                var relativePath = item.ValueKind == JsonValueKind.String
                    ? item.GetString()
                    : item.TryGetProperty("path", out var path) ? path.GetString() : null;
                if (string.IsNullOrWhiteSpace(relativePath))
                {
                    blockers.Add("Offline verification contains an invalid evidence path.");
                    continue;
                }

                var fullPath = ResolveInputPath(root, relativePath);
                if (!File.Exists(fullPath))
                {
                    blockers.Add($"Offline verification evidence is missing: {relativePath}");
                    continue;
                }

                paths.Add(fullPath);
                inputs.Add(await DigestFileAsync(root, fullPath, cancellationToken));
            }

            foreach (var arm in Arms)
            {
                if (!paths.Any(path => path.Contains(arm, StringComparison.OrdinalIgnoreCase)))
                {
                    blockers.Add($"Offline verification has no saved synthetic evidence for '{arm}'.");
                }
            }

            if (marker.TryGetProperty("executableDigests", out var executableDigests)
                && executableDigests.ValueKind == JsonValueKind.Object
                && executableDigests.EnumerateObject().Count() == bundles.Count)
            {
                foreach (var bundle in bundles)
                {
                    if (!executableDigests.TryGetProperty(bundle.Name, out var digest)
                        || digest.GetString() != bundle.Files.FirstOrDefault(
                            file => Path.GetFullPath(file.Path) == Path.GetFullPath(bundle.Executable))?.Sha256)
                    {
                        blockers.Add($"Offline verification executable digest does not match the retained '{bundle.Name}' bundle.");
                    }
                }
            }
            else
            {
                blockers.Add("Offline verification has no executableDigests object.");
            }

            if (!marker.TryGetProperty("bundleDigests", out var bundleDigests)
                || bundleDigests.ValueKind != JsonValueKind.Object
                || bundleDigests.EnumerateObject().Count() != bundles.Count)
            {
                blockers.Add("Offline verification has no full bundleDigests object.");
            }
            else
            {
                foreach (var bundle in bundles)
                {
                    var directory = Path.GetDirectoryName(bundle.Executable)!;
                    var digest = ComparisonBundleInventory.Digest(ComparisonBundleInventory.Files(directory));
                    if (!bundleDigests.TryGetProperty(bundle.Name, out var expected)
                        || expected.ValueKind != JsonValueKind.String
                        || !string.Equals(expected.GetString(), digest, StringComparison.OrdinalIgnoreCase))
                    {
                        blockers.Add($"Offline verification full bundle digest does not match retained '{bundle.Name}'.");
                    }
                }
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or IOException or InvalidDataException)
        {
            blockers.Add($"Offline verification evidence is invalid ({exception.GetType().Name}).");
        }
    }

    private static async Task ValidateOfflineVerificationAsync(ComparisonLaunchPlan plan, List<string> blockers, CancellationToken cancellationToken)
    {
        var marker = plan.Inputs.FirstOrDefault(input => input.Path.EndsWith("offline-verification.json", StringComparison.Ordinal));
        if (marker is null)
        {
            blockers.Add("The plan is not bound to offline verification evidence.");
            return;
        }

        try
        {
            var markerPath = ResolveInputPath(plan.RepositoryRoot, marker.Path);
            await using var stream = File.OpenRead(markerPath);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var markerJson = document.RootElement;
            ValidateOfflineMarkerFields(markerJson, blockers);
            if (!markerJson.TryGetProperty("evidencePaths", out var evidencePaths) || evidencePaths.ValueKind != JsonValueKind.Array)
            {
                blockers.Add("Offline verification evidencePaths must be an array.");
                return;
            }

            var evidenceFiles = new List<string>();
            foreach (var item in evidencePaths.EnumerateArray())
            {
                var relativePath = item.ValueKind == JsonValueKind.String
                    ? item.GetString()
                    : item.TryGetProperty("path", out var path) ? path.GetString() : null;
                if (string.IsNullOrWhiteSpace(relativePath))
                {
                    blockers.Add("Offline verification contains an invalid evidence path.");
                    continue;
                }

                var fullPath = ResolveInputPath(plan.RepositoryRoot, relativePath);
                var input = plan.Inputs.FirstOrDefault(value => Path.GetFullPath(value.Path, plan.RepositoryRoot) == fullPath);
                if (input is null || !File.Exists(fullPath)
                    || !string.Equals(await HashFileAsync(fullPath, cancellationToken), input.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    blockers.Add($"Offline verification evidence is not hash-bound or changed: {relativePath}");
                    continue;
                }

                evidenceFiles.Add(fullPath);
            }

            foreach (var arm in Arms)
            {
                if (!evidenceFiles.Any(path => path.Contains(arm, StringComparison.OrdinalIgnoreCase)))
                {
                    blockers.Add($"Offline verification has no hash-bound synthetic evidence for '{arm}'.");
                }
            }

            if (!markerJson.TryGetProperty("executableDigests", out var executableDigests)
                || executableDigests.ValueKind != JsonValueKind.Object
                || executableDigests.EnumerateObject().Count() != plan.Bundles.Count)
            {
                blockers.Add("Offline verification has no executableDigests object.");
            }
            else
            {
                foreach (var bundle in plan.Bundles)
                {
                    var executableDigest = bundle.Files.FirstOrDefault(
                        file => Path.GetFullPath(file.Path) == Path.GetFullPath(bundle.Executable))?.Sha256;
                    if (executableDigest is null || !executableDigests.TryGetProperty(bundle.Name, out var digest)
                        || digest.GetString() != executableDigest)
                    {
                        blockers.Add($"Offline verification executable digest does not match the retained '{bundle.Name}' bundle.");
                    }
                }

            }

            if (!markerJson.TryGetProperty("bundleDigests", out var bundleDigests)
                || bundleDigests.ValueKind != JsonValueKind.Object
                || bundleDigests.EnumerateObject().Count() != plan.Bundles.Count)
            {
                blockers.Add("Offline verification has no full bundleDigests object.");
            }
            else
            {
                foreach (var bundle in plan.Bundles)
                {
                    var directory = Path.GetDirectoryName(bundle.Executable)!;
                    var digest = ComparisonBundleInventory.Digest(ComparisonBundleInventory.Files(directory));
                    if (!bundleDigests.TryGetProperty(bundle.Name, out var expected)
                        || expected.ValueKind != JsonValueKind.String
                        || !string.Equals(expected.GetString(), digest, StringComparison.OrdinalIgnoreCase))
                    {
                        blockers.Add($"Offline verification full bundle digest does not match retained '{bundle.Name}'.");
                    }
                }
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or IOException or InvalidDataException)
        {
            blockers.Add($"Offline verification evidence could not be revalidated ({exception.GetType().Name}).");
        }
    }

    private static void ValidateOfflineMarkerFields(JsonElement marker, List<string> blockers)
    {
        if (!marker.TryGetProperty("schemaVersion", out var schemaVersion) || schemaVersion.GetInt32() != 1
            || !marker.TryGetProperty("synthetic", out var synthetic) || synthetic.ValueKind != JsonValueKind.True
            || !marker.TryGetProperty("liveExecutionStarted", out var live) || live.ValueKind != JsonValueKind.False
            || !marker.TryGetProperty("passed", out var passed) || passed.ValueKind != JsonValueKind.True)
        {
            blockers.Add("Offline verification must be successful, synthetic, and explicitly marked as having no live execution.");
        }

        if (!marker.TryGetProperty("executionId", out var executionId)
            || executionId.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(executionId.GetString()))
        {
            blockers.Add("Offline verification must identify its synthetic execution.");
        }

        if (!marker.TryGetProperty("arms", out var arms) || arms.ValueKind != JsonValueKind.Array
            || arms.GetArrayLength() != Arms.Length
            || !arms.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String)
            || !arms.EnumerateArray().Select(item => item.GetString()).ToHashSet(StringComparer.Ordinal).SetEquals(Arms))
        {
            blockers.Add("Offline verification must cover exactly the three comparison arms.");
        }
    }

    private static void CopyRuntimeDirectory(string source, string destination)
    {
        if (Directory.Exists(destination))
        {
            throw new IOException("A retained runtime bundle already exists at the destination.");
        }

        Directory.CreateDirectory(destination);
        foreach (var file in ComparisonBundleInventory.Files(source))
        {
            var target = Path.Combine(destination, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(Path.Combine(source, file.Path), target, overwrite: true);
        }
    }

    private static Task<IReadOnlyList<FileDigest>> InventoryDirectoryAsync(string directory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<FileDigest> inventory = ComparisonBundleInventory.Files(directory)
            .Select(file => file with { Path = Path.GetFullPath(Path.Combine(directory, file.Path)) })
            .ToArray();
        return Task.FromResult(inventory);
    }

    private static async Task<FileDigest> DigestFileAsync(string root, string path, CancellationToken cancellationToken) =>
        new(Path.GetRelativePath(root, Path.GetFullPath(path)), await HashFileAsync(path, cancellationToken));

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string ResolveInputPath(string root, string path)
    {
        var fullPath = Path.GetFullPath(path, root);
        if (!IsWithin(fullPath, root) || HasReparsePoint(fullPath))
        {
            throw new InvalidDataException($"Prepared input escapes its repository root or uses a symbolic link: {path}");
        }

        return fullPath;
    }

    private static bool IsWithin(string path, string parent)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(fullParent, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static bool HasReparsePoint(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var current = File.Exists(fullPath) ? Path.GetDirectoryName(fullPath) : fullPath;
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }

            var parent = Path.GetDirectoryName(current);
            if (string.Equals(parent, current, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                break;
            }

            current = parent;
        }

        return File.Exists(fullPath) && (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0;
    }

    private static async Task<bool> IsWritableDirectoryAsync(string directory, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, $".preflight-{Guid.NewGuid():N}.tmp");
            await File.WriteAllTextAsync(probe, string.Empty, cancellationToken);
            File.Delete(probe);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async Task<string> ComputeDirtyDigestAsync(string root, CancellationToken cancellationToken)
    {
        var diff = await RunGitAsync(root, ["diff", "--binary", "HEAD"], cancellationToken);
        var status = await RunGitAsync(root, ["status", "--porcelain", "--untracked-files=all"], cancellationToken);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(diff));
        foreach (var line in status.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.StartsWith("?? ", StringComparison.Ordinal))
            {
                continue;
            }

            var relativePath = line[3..];
            if (relativePath.StartsWith(".artifacts/", StringComparison.Ordinal) || relativePath.StartsWith(".artifacts\\", StringComparison.Ordinal))
            {
                continue;
            }

            var fullPath = Path.GetFullPath(relativePath, root);
            if (!File.Exists(fullPath))
            {
                continue;
            }

            hash.AppendData(Encoding.UTF8.GetBytes(relativePath));
            await using var stream = File.OpenRead(fullPath);
            var bytes = await SHA256.HashDataAsync(stream, cancellationToken);
            hash.AppendData(bytes);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static async Task<string> GitAsync(string root, IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        (await RunGitAsync(root, arguments, cancellationToken)).Trim();

    private static async Task<string> RunGitAsync(string root, IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        await ProcessAsync("git", arguments, root, cancellationToken);

    private static async Task<string> ProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start '{fileName}'.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await stdout;
        var error = await stderr;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"'{fileName}' exited with code {process.ExitCode}: {error.Trim()}");
        }

        return output;
    }

    /// <summary>Represents one frozen corpus tier and its launch limits.</summary>
    /// <param name="Name">The corpus tier name.</param>
    /// <param name="ManifestName">The source manifest file name.</param>
    /// <param name="MaxAgentCalls">The main-agent call allowance.</param>
    /// <param name="WrapUpCalls">The reserved wrap-up calls.</param>
    /// <param name="TreatmentWindow">The TokenGuard treatment context window.</param>
    internal sealed record Tier(string Name, string ManifestName, int MaxAgentCalls, int WrapUpCalls, int TreatmentWindow);

    private sealed record CatalogLimits(int ContextWindowTokens, int MaxCompletionTokens);
}
