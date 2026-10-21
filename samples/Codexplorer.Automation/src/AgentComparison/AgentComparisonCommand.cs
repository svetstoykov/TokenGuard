using Codexplorer.Automation.AgentComparison.Recording;

namespace Codexplorer.Automation.AgentComparison;

/// <summary>Dispatches comparison setup and explicitly guarded future execution.</summary>
internal static class AgentComparisonCommand
{
    /// <summary>Asynchronously executes a comparison command before legacy host construction.</summary>
    /// <param name="args">The mode and explicit options.</param>
    /// <param name="output">The redacted command output.</param>
    /// <returns>A task containing the command exit code.</returns>
    internal static async Task<int> RunAsync(string[] args, TextWriter output)
    {
        try
        {
            if (args.Length == 0)
                throw new ArgumentException("Expected prepare, verify-offline, preflight or run.");
            var mode = args[0];
            var values = ParseOptions(args[1..]);
            if (mode == "run" && !values.ContainsKey("--execute-live"))
                throw new InvalidOperationException("Live execution requires --execute-live and a later user instruction authorizing the campaign.");
            if (mode == "compare")
            {
                RequireOnly(values, "--reports", "--baseline");
                return await ComparisonReportCommand.RunAsync(Required(values, "--reports"), values.GetValueOrDefault("--baseline"),
                    output, CancellationToken.None);
            }
            var root = FindRepositoryRoot();
            IComparisonCredentialResolver credentials = new ComparisonCredentialResolver(FindCredentialExecutable(root));
            ILaunchPreparation preparation = new LaunchPreparation(credentials);
            IComparisonCampaign campaign = new ComparisonCampaign(preparation, credentials);
            switch (mode)
            {
                case "prepare":
                    RequireOnly(values, "--output", "--repetitions");
                    var repetitions = values.TryGetValue("--repetitions", out var text) ? int.Parse(text!) : 1;
                    var plan = await preparation.PrepareAsync(root, Required(values, "--output"), repetitions, CancellationToken.None);
                    await output.WriteLineAsync($"Prepared {plan.Sessions.Count} future sessions; liveExecutionStarted=false.");
                    foreach (var blocker in plan.Blockers)
                        await output.WriteLineAsync($"BLOCKER: {blocker}");
                    return 0;
                case "verify-offline":
                    RequireOnly(values, "--output");
                    return await campaign.VerifyOfflineAsync(root, Required(values, "--output"), output, CancellationToken.None);
                case "preflight":
                    RequireOnly(values, "--plan");
                    var readiness = await preparation.PreflightAsync(Required(values, "--plan"), CancellationToken.None);
                    await output.WriteLineAsync(
                        $"ready={readiness.Ready}; credentialPresent={readiness.CredentialPresent}; liveExecutionStarted=false.");
                    foreach (var blocker in readiness.Blockers)
                        await output.WriteLineAsync($"BLOCKER: {blocker}");
                    return readiness.Ready ? 0 : 1;
                case "run":
                    RequireOnly(values, "--plan", "--execute-live");
                    return await campaign.RunAsync(Required(values, "--plan"), output, CancellationToken.None);
                default:
                    throw new ArgumentException("Unknown agent-comparison mode.");
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException or IOException
            or InvalidOperationException or System.Text.Json.JsonException or FormatException or OverflowException)
        {
            await output.WriteLineAsync($"Comparison command refused ({exception.GetType().Name}): {SafeMessage(exception)}");
            return 1;
        }
    }

    /// <summary>Finds the source worktree from the current directory.</summary>
    /// <returns>The containing repository root.</returns>
    /// <exception cref="InvalidOperationException">The command is outside a TokenGuard checkout.</exception>
    internal static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(Environment.CurrentDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "TokenGuard.sln")))
                return current.FullName;
        throw new InvalidOperationException("Run agent-comparison from the TokenGuard worktree.");
    }

    /// <summary>Finds the original sample development-settings location without copying its file.</summary>
    /// <param name="root">The current worktree.</param>
    /// <returns>An executable-location marker whose adjacent ignored settings supply credential fallback.</returns>
    internal static string FindCredentialExecutable(string root)
    {
        var originalRoot = root;
        var gitPointer = Path.Combine(root, ".git");
        if (File.Exists(gitPointer))
        {
            var gitDirectory = File.ReadAllText(gitPointer).Trim()[8..];
            var commonPointer = Path.Combine(gitDirectory, "commondir");
            if (File.Exists(commonPointer))
                originalRoot = Directory.GetParent(Path.GetFullPath(File.ReadAllText(commonPointer).Trim(), gitDirectory))!.FullName;
        }
        var projectDirectory = Path.Combine(originalRoot, "samples", "Codexplorer", "src");
        if (File.Exists(Path.Combine(projectDirectory, "appsettings.Development.json")))
            return Path.Combine(projectDirectory, "Codexplorer");
        return Path.Combine(originalRoot, "samples", "Codexplorer", "src", "bin", "Release", "net10.0", "Codexplorer");
    }

    private static Dictionary<string, string?> ParseOptions(string[] args)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index++)
        {
            var name = args[index];
            if (!name.StartsWith("--", StringComparison.Ordinal) || values.ContainsKey(name))
                throw new ArgumentException("Options must be unique explicit names.");
            if (name == "--execute-live")
                values.Add(name, null);
            else if (++index < args.Length && !args[index].StartsWith("--", StringComparison.Ordinal))
                values.Add(name, args[index]);
            else
                throw new ArgumentException("A comparison option is missing its value.");
        }
        return values;
    }

    private static string Required(Dictionary<string, string?> values, string name) =>
        values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value : throw new ArgumentException($"Missing required option {name}.");

    private static void RequireOnly(Dictionary<string, string?> values, params string[] allowed)
    {
        if (values.Keys.Except(allowed, StringComparer.Ordinal).Any())
            throw new ArgumentException("The comparison mode received an unsupported option.");
    }

    private static string SafeMessage(Exception exception) => exception is InvalidOperationException or ArgumentException
        ? exception.Message : "Inspect the local readiness artifacts for details.";
}
