using System.Diagnostics;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Codexplorer.Automation.AgentComparison.Codexplorer;
using Codexplorer.Automation.AgentComparison.OpenCode;
using Codexplorer.Automation.AgentComparison.Recording;
using Codexplorer.Automation.Configuration;
using Codexplorer.Automation.Scoring;

namespace Codexplorer.Automation.AgentComparison;

/// <summary>Runs isolated sessions and keeps protocol outcomes independent from scoring and usage.</summary>
/// <remarks>One campaign runs sequentially; offline mode constructs only a local fake provider boundary.</remarks>
internal sealed class ComparisonCampaign(ILaunchPreparation preparation, IComparisonCredentialResolver credentials) : IComparisonCampaign
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] Arms = ["codexplorer-control", "codexplorer-treatment", "opencode"];

    /// <inheritdoc />
    public async Task<int> VerifyOfflineAsync(string root, string output, TextWriter writer, CancellationToken cancellationToken)
    {
        var outputRoot = ResolveOutput(root, output);
        var executionId = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "-" + Guid.NewGuid().ToString("N")[..8];
        var evidenceRoot = Path.Combine(outputRoot, "scratch", "offline", executionId);
        var bundles = LocateBuiltBundles(root, outputRoot);
        var missing = bundles.Where(bundle => !File.Exists(bundle.Executable)).Select(bundle => bundle.Name).ToArray();
        if (missing.Length > 0)
        {
            await writer.WriteLineAsync($"Offline verification blocked: missing runtime executable(s): {string.Join(", ", missing)}.");
            return 1;
        }
        var settings = new ComparisonSettings("qwen/qwen3.7-flash", "alibaba", 32768, true)
        {
            ProviderContextTokens = 1_000_000, ProviderMaxCompletionTokens = 32768
        };
        await WriteJsonAsync(Path.Combine(evidenceRoot, "manifest.json"), new
        {
            schemaVersion = 1, synthetic = true, liveExecutionStarted = false, executionId, settings, arms = Arms,
            bundles = bundles.Select(bundle => new { bundle.Name, bundle.Executable, sha256 = ComparisonArtifacts.Hash(bundle.Executable) })
        }, cancellationToken);
        var reports = new List<ComparisonExecutionReport>();
        var evidencePaths = new List<string>();
        foreach (var arm in Arms)
        {
            var sessionRoot = Path.Combine(evidenceRoot, arm, "synthetic-session");
            var checkout = Path.Combine(sessionRoot, "checkout");
            var artifacts = Path.Combine(sessionRoot, "artifacts");
            Directory.CreateDirectory(checkout);
            Directory.CreateDirectory(artifacts);
            await GitAsync(checkout, ["init", "--quiet"], cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(checkout, "README.md"), "SYNTHETIC_FIXTURE=verified\n", cancellationToken);
            await GitAsync(checkout, ["add", "README.md"], cancellationToken);
            await GitAsync(checkout, ["-c", "user.name=Offline Fixture", "-c", "user.email=offline@invalid", "commit", "--quiet", "-m", "fixture"],
                cancellationToken);
            var repositorySha = (await GitAsync(checkout, ["rev-parse", "HEAD"], cancellationToken)).Trim();
            var task = new AutomationTaskDefinition
            {
                TaskId = "synthetic-local-tool-cycle", Title = "Synthetic local tool cycle", RepositoryCommit = repositorySha,
                TaskSize = RunnerTaskSize.Small,
                InitialPrompt = "Read README.md, record SYNTHETIC_FIXTURE=verified in evidence.md using the artifact tools, and report verified.",
                Checks =
                [
                    new AutomationCheckDefinition { Id = "evidence", Kind = "contains", Artifact = "evidence.md", AnyOf = ["verified"] },
                    new AutomationCheckDefinition { Id = "answer", Kind = "contains", AnyOf = ["verified"] }
                ]
            };
            var promptHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(task.InitialPrompt)));
            var session = new ComparisonSessionPlan($"synthetic-{arm}", "synthetic", 1, arm, task, task.InitialPrompt,
                promptHash, promptHash, 5, 1, arm == "opencode" ? null : 1_000_000);
            var bundle = bundles.Single(bundle => bundle.Name == (arm == "opencode" ? "opencode" : "codexplorer"));
            var fake = new FakeOpenRouterProvider(CreateScript(arm, checkout, artifacts));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));
            var report = await ExecuteAsync(session, bundle.Executable, sessionRoot, settings, "synthetic", fake, null, timeout.Token);
            reports.Add(report);
            var reportPath = Path.Combine(sessionRoot, "report.json");
            evidencePaths.Add(Path.GetRelativePath(root, reportPath));
            foreach (var capture in Directory.EnumerateFiles(sessionRoot, "*", SearchOption.AllDirectories))
            {
                if (capture.Contains(Path.Combine("state", "runtime"), StringComparison.Ordinal)
                    || capture.Contains(Path.DirectorySeparatorChar + "checkout" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    continue;
                evidencePaths.Add(Path.GetRelativePath(root, capture));
            }
            await writer.WriteLineAsync($"SYNTHETIC {arm}: {report.Session.Runtime.Outcome}; attempts={report.Usage.AttemptCount}; "
                + $"checks={report.Session.EvidenceChecks.Concat(report.Session.FinalAnswerChecks).Count(check => check.Passed)}/2.");
        }
        var passed = reports.Count == 3 && reports.All(report => report.Session.Runtime.Outcome == "completed"
            && !report.Session.Runtime.Partial && report.Session.EvidenceChecks.Count == 1 && report.Session.FinalAnswerChecks.Count == 1
            && report.Session.EvidenceChecks.Concat(report.Session.FinalAnswerChecks).All(check => check.Passed)
            && report.Usage.CompleteTokens && report.Attempts.Any(attempt => ContainsToolResult(attempt.RequestBody)));
        IComparisonFixtureEvidence fixtureEvidence = new ComparisonFixtureEvidence();
        var verifiedFixtures = fixtureEvidence.FindVerified(outputRoot, bundles);
        if (verifiedFixtures.Count == 0)
        {
            passed = false;
            await writer.WriteLineAsync(
                "BLOCKER: run the automation tests with installed runtimes to retain compaction, denial and failure receipts.");
        }
        evidencePaths.AddRange(verifiedFixtures.Select(path => Path.GetRelativePath(root, path)));
        var markerPath = Path.Combine(outputRoot, "scratch", "offline-verification.json");
        Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
        await WriteJsonAsync(markerPath, new
        {
            schemaVersion = 1, synthetic = true, liveExecutionStarted = false, passed,
            arms = Arms, executionId, evidencePaths = evidencePaths.Distinct().ToArray(),
            executableDigests = bundles.ToDictionary(bundle => bundle.Name, bundle => ComparisonArtifacts.Hash(bundle.Executable)),
            bundleDigests = bundles.ToDictionary(bundle => bundle.Name,
                bundle => ComparisonBundleInventory.Digest(ComparisonBundleInventory.Files(Path.GetDirectoryName(bundle.Executable)!))),
            limitations = new[] { "Synthetic conversations verify boundaries; they provide no performance conclusion." }
        }, cancellationToken);
        await WriteJsonAsync(Path.Combine(evidenceRoot, "comparison.json"), reports, cancellationToken);
        await WriteJsonAsync(Path.Combine(evidenceRoot, "verification.json"), new
        {
            schemaVersion = 1, synthetic = true, liveExecutionStarted = false, passed, executionId, evidencePaths
        }, cancellationToken);
        await File.WriteAllLinesAsync(Path.Combine(evidenceRoot, "invocation.log"), reports.Select(report =>
            $"SYNTHETIC {report.Session.Session.Arm}: {report.Session.Runtime.Outcome}; attempts={report.Usage.AttemptCount}"), cancellationToken);
        await writer.WriteLineAsync($"Synthetic verification passed={passed}; liveExecutionStarted=false; marker={markerPath}");
        return passed ? 0 : 1;
    }

    /// <inheritdoc />
    public async Task<int> RunAsync(string planPath, TextWriter writer, CancellationToken cancellationToken)
    {
        var readiness = await preparation.PreflightAsync(planPath, cancellationToken);
        if (!readiness.Ready)
            throw new InvalidOperationException("Live execution refused: preflight has unresolved readiness blockers.");
        var plan = await ComparisonPlanFile.LoadAsync(planPath, cancellationToken);
        if (readiness.PlanDigest != plan.PlanDigest)
            throw new InvalidOperationException("Live execution refused: readiness does not bind this complete plan.");
        var credential = credentials.Resolve();
        if (!credential.Present || string.IsNullOrWhiteSpace(credential.ApiKey))
            throw new InvalidOperationException("Live execution refused: the existing credential is unavailable.");
        var outputRoot = ResolveOutput(plan.RepositoryRoot, plan.OutputRoot);
        var executionId = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "-" + Guid.NewGuid().ToString("N")[..8];
        var failed = false;
        foreach (var session in plan.Sessions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            readiness = await preparation.PreflightAsync(planPath, cancellationToken);
            if (!readiness.Ready || readiness.PlanDigest != plan.PlanDigest)
                throw new InvalidOperationException("Live execution stopped: launch provenance changed before the next session.");
            var armFolder = session.Arm == "opencode" ? Path.Combine("pilots", "opencode")
                : session.Arm == "codexplorer-control" ? "control" : "treatment";
            var invocationRoot = Path.Combine(outputRoot, armFolder, executionId);
            var sessionRoot = ComparisonArtifacts.Resolve(invocationRoot, session.Id);
            Directory.CreateDirectory(sessionRoot);
            await WriteJsonAsync(Path.Combine(invocationRoot, "manifest.json"), plan, cancellationToken);
            var checkout = Path.Combine(sessionRoot, "checkout");
            RuntimeResult? stagingFailure = null;
            try
            {
                await StageRepositoryAsync(session, checkout, cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or OperationCanceledException
                or UnauthorizedAccessException or Win32Exception)
            {
                stagingFailure = new RuntimeResult(cancellationToken.IsCancellationRequested ? "cancelled" : "failed",
                    true, null, 0, [], exception.GetType().Name);
            }
            var executable = plan.Bundles.Single(bundle => bundle.Name == (session.Arm == "opencode" ? "opencode" : "codexplorer")).Executable;
            var report = await ExecuteAsync(session, executable, sessionRoot, plan.Settings, plan.PlanDigest, null,
                credential.ApiKey, cancellationToken, stagingFailure, async token =>
                {
                    var current = await preparation.PreflightAsync(planPath, token).ConfigureAwait(false);
                    return current.Ready && current.PlanDigest == plan.PlanDigest;
                });
            await WriteJsonAsync(Path.Combine(invocationRoot, session.Id + ".report.json"), report, CancellationToken.None);
            await File.AppendAllTextAsync(Path.Combine(invocationRoot, "invocation.log"),
                $"{session.Id}: {report.Session.Runtime.Outcome}\n", CancellationToken.None);
            failed |= report.Session.Runtime.Outcome != "completed";
            await writer.WriteLineAsync($"{session.Id}: {report.Session.Runtime.Outcome}; timeline={report.Session.RequestTimelinePath}");
            if (cancellationToken.IsCancellationRequested)
                return 1;
        }
        return failed ? 1 : 0;
    }

    /// <summary>Asynchronously records a full session or a staging failure without losing partial outcomes.</summary>
    /// <param name="session">The immutable task contract.</param>
    /// <param name="executable">The retained runtime executable.</param>
    /// <param name="sessionRoot">The isolated invocation directory.</param>
    /// <param name="settings">The upstream settings.</param>
    /// <param name="planDigest">The bound plan digest.</param>
    /// <param name="fake">The local fake, or null for an authorized live invocation.</param>
    /// <param name="secret">The in-memory upstream credential, or null for a fake.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="stagingFailure">The pre-runtime failure to retain, or null to invoke the runtime.</param>
    /// <param name="authorizeForward">The fresh readiness check, required for a live forwarding boundary.</param>
    /// <returns>A task containing the persisted complete or partial evidence.</returns>
    internal static async Task<ComparisonExecutionReport> ExecuteAsync(ComparisonSessionPlan session, string executable, string sessionRoot,
        ComparisonSettings settings, string planDigest, FakeOpenRouterProvider? fake, string? secret, CancellationToken cancellationToken,
        RuntimeResult? stagingFailure = null, Func<CancellationToken, Task<bool>>? authorizeForward = null)
    {
        var started = DateTimeOffset.UtcNow;
        var artifacts = Path.Combine(sessionRoot, "artifacts");
        var stateRoot = Path.Combine(sessionRoot, "state");
        var recordingRoot = Path.Combine(sessionRoot, "recording");
        Directory.CreateDirectory(artifacts);
        Directory.CreateDirectory(stateRoot);
        await using IRecordingProxy proxy = new RecordingProxy(new RecordingProxyOptions(
            fake is null ? RecordingProxyMode.Live : RecordingProxyMode.Fake,
            fake is null ? new Uri("https://openrouter.ai/api/v1/") : null, secret, fake, recordingRoot, settings)
        {
            AuthorizeForwardAsync = authorizeForward
        });
        var checkout = Path.Combine(sessionRoot, "checkout");
        var result = stagingFailure;
        if (result is null)
        {
            try
            {
                await proxy.StartAsync(cancellationToken);
                IRecordingScope scope = proxy.BeginScope(new RecordingScopeOptions(session.Id, session.MaxAgentCalls, session.WrapUpCalls,
                    "local-" + Guid.NewGuid().ToString("N")));
                var invocation = new RuntimeInvocation(session, executable, checkout, artifacts, stateRoot,
                    proxy.Endpoint, scope.Credential, settings, fake is not null);
                IComparisonHelper helper = new ComparisonHelper(scope);
                IComparisonRuntime runtime = session.Arm == "opencode" ? new OpenCodeRuntime(scope, helper) : new CodexplorerRuntime(scope, helper);
                result = await runtime.RunAsync(invocation, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                result = new RuntimeResult(exception is OperationCanceledException ? "cancelled" : "failed", true,
                    null, 0, [], exception.GetType().Name);
            }
        }
        await proxy.DrainAsync(CancellationToken.None).ConfigureAwait(false);
        if (proxy.CaptureFailure is not null)
            result = result with { Outcome = "failed", Partial = true, Error = "Capture persistence failed: " + proxy.CaptureFailure };
        var captureReferences = result.CapturePaths.Select(path => Path.IsPathRooted(path)
            ? Path.GetRelativePath(sessionRoot, path) : Path.Combine("artifacts", path)).ToList();
        foreach (var path in Directory.EnumerateFiles(stateRoot, "*.json*", SearchOption.TopDirectoryOnly))
            captureReferences.Add(Path.GetRelativePath(sessionRoot, path));
        result = result with { CapturePaths = captureReferences.Distinct(StringComparer.Ordinal).ToArray() };
        IComparisonScoring scorer = new ComparisonScoring(new AnswerScorer());
        ComparisonCheckResults checks;
        IReadOnlyList<FileDigest> inventory;
        try
        {
            checks = scorer.Score(session, artifacts, result.FinalAnswer);
            inventory = ComparisonArtifacts.Inventory(artifacts);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            result = result with { Outcome = "failed", Partial = true, Error = "Artifact collection failed: " + exception.GetType().Name };
            checks = new ComparisonCheckResults((session.Task.Checks ?? []).Where(check => check.Artifact is not null)
                .Select(check => new Reporting.CheckResult { Id = check.Id!, Passed = false, Reason = "artifactMissing" }).ToArray(),
                (session.Task.Checks ?? []).Where(check => check.Artifact is null)
                    .Select(check => new AnswerScorer().EvaluateCheck(check, result.FinalAnswer, "noAnswer")).ToArray());
            inventory = [];
        }
        var diffPath = Path.Combine(sessionRoot, "repository.diff");
        string diff;
        string changedPaths;
        try
        {
            changedPaths = await GitAsync(checkout, ["status", "--porcelain=v1", "--untracked-files=all"], CancellationToken.None);
            await GitAsync(checkout, ["add", "--intent-to-add", "--", "."], CancellationToken.None);
            diff = await GitAsync(checkout, ["diff", "--binary", "HEAD"], CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or Win32Exception or UnauthorizedAccessException)
        {
            diff = "Repository diff unavailable after staging or checkout failure.";
            changedPaths = "unavailable";
        }
        await File.WriteAllTextAsync(diffPath, diff, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(sessionRoot, "changed-paths.txt"), changedPaths, CancellationToken.None);
        if (result.FinalAnswer is not null)
            await File.WriteAllTextAsync(Path.Combine(sessionRoot, "final-answer.md"), result.FinalAnswer, CancellationToken.None);
        var attempts = proxy.Attempts;
        var report = new ComparisonExecutionReport(1, fake is not null, fake is null && attempts.Any(attempt => !attempt.BudgetRejected), planDigest,
            new ComparisonSessionReport(session, started, DateTimeOffset.UtcNow, result, checks.Evidence, checks.FinalAnswer, inventory,
                "repository.diff", "timeline.md"), ComparisonAccounting.Aggregate(attempts), attempts,
            ["Checks score notes and answers; they do not prove implementation correctness.",
                "Unknown request classifications conservatively consume the main-agent allowance.",
                "Context byte sizes are measurements; per-message provider token attribution is unavailable."]);
        await WriteJsonAsync(Path.Combine(sessionRoot, "report.json"), report, CancellationToken.None);
        await ComparisonDiagnostics.WriteAsync(Path.Combine(sessionRoot, "timeline.md"), report, CancellationToken.None);
        if (fake is not null)
            await File.WriteAllLinesAsync(Path.Combine(sessionRoot, "fake-provider-journal.jsonl"),
                fake.Requests.Select(bytes => Encoding.UTF8.GetString(bytes)), CancellationToken.None);
        return report;
    }

    private static IEnumerable<FakeCompletion> CreateScript(string arm, string checkout, string artifacts)
    {
        using var usageDocument = JsonDocument.Parse("""
            {"prompt_tokens":100,"completion_tokens":20,"total_tokens":120,"cost":0.0001,
             "prompt_tokens_details":{"cached_tokens":40},"completion_tokens_details":{"reasoning_tokens":5}}
            """);
        var usage = usageDocument.RootElement.Clone();
        var openCode = arm == "opencode";
        var readArgs = openCode ? JsonSerializer.Serialize(new { filePath = Path.Combine(checkout, "README.md"), offset = 1, limit = 5 })
            : JsonSerializer.Serialize(new { path = "README.md" });
        var writeArgs = openCode
            ? JsonSerializer.Serialize(new { filePath = Path.Combine(artifacts, "evidence.md"), content = "SYNTHETIC_FIXTURE=verified" })
            : JsonSerializer.Serialize(new { path = "evidence.md", content = "SYNTHETIC_FIXTURE=verified" });
        yield return new FakeCompletion(null, [new FakeToolCall("read-1", openCode ? "read" : "read_file", readArgs)], usage, arm + "-1");
        yield return new FakeCompletion(null, [new FakeToolCall("write-1", openCode ? "write" : "create_artifact", writeArgs)], usage, arm + "-2");
        yield return new FakeCompletion("Synthetic findings verified.", [], usage, arm + "-3");
        yield return new FakeCompletion("Synthetic findings verified; continued.", [], usage, arm + "-4");
        yield return new FakeCompletion("Synthetic final findings verified.", [], usage, arm + "-5");
    }

    private static RuntimeBundle[] LocateBuiltBundles(string root, string outputRoot) =>
    [
        new("opencode", Path.Combine(outputRoot, "scratch", "tools", "opencode-v1.19.3", "opencode"), "1.19.3", []),
        new("codexplorer", Path.Combine(root, "samples", "Codexplorer", "src", "bin", "Release", "net10.0", "Codexplorer"), "source", []),
        new("automation", Path.Combine(root, "samples", "Codexplorer.Automation", "src", "bin", "Release", "net10.0", "Codexplorer.Automation"),
            "source", [])
    ];

    private static string ResolveOutput(string root, string output)
    {
        var approved = Path.Combine(root, ".artifacts", "reports", "opencode-comparison");
        var path = Path.GetFullPath(output, root);
        ComparisonArtifacts.Resolve(approved, Path.GetRelativePath(approved, path));
        return path;
    }

    private static bool ContainsToolResult(byte[] body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.TryGetProperty("messages", out var messages)
            && messages.EnumerateArray().Any(message => message.TryGetProperty("role", out var role) && role.GetString() == "tool");
    }

    private static async Task StageRepositoryAsync(ComparisonSessionPlan session, string checkout, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(checkout);
        await GitAsync(checkout, ["init", "--quiet"], cancellationToken);
        await GitAsync(checkout, ["fetch", "--depth=1", "--no-tags", session.Task.RepositoryUrl!, session.Task.RepositoryCommit!], cancellationToken);
        await GitAsync(checkout, ["checkout", "--detach", "FETCH_HEAD"], cancellationToken);
        var sha = (await GitAsync(checkout, ["rev-parse", "HEAD"], cancellationToken)).Trim();
        if (sha != session.Task.RepositoryCommit)
            throw new InvalidDataException("The isolated repository did not resolve the frozen commit.");
    }

    private static async Task<string> GitAsync(string directory, IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
        };
        start.Environment.Clear();
        start.Environment["PATH"] = "/usr/bin:/bin";
        start.Environment["HOME"] = directory;
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_CONFIG_GLOBAL"] = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("core.hooksPath=/dev/null");
        foreach (var argument in args)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Could not start isolated git.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var text = await stdout.ConfigureAwait(false);
            await stderr.ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw new IOException("The isolated repository git command failed.");
            return text;
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
    }

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions), cancellationToken);
    }
}
