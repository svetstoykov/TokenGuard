using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Codexplorer.Automation.AgentComparison;
using Codexplorer.Automation.AgentComparison.Codexplorer;
using Codexplorer.Automation.AgentComparison.Recording;
using Codexplorer.Automation.Configuration;
using FluentAssertions;

namespace Codexplorer.Automation.Tests.AgentComparison.Codexplorer;

/// <summary>Verifies the comparison boundary and its observable outcomes.</summary>
public sealed class CodexplorerPinnedRuntimeTests
{
    /// <summary>Verifies pinned runtime compacts synthetic tool history continues and wraps up.</summary>
    [PinnedCodexplorerFact]
    public async Task PinnedRuntime_CompactsSyntheticToolHistoryContinuesAndWrapsUp()
    {
        var root = FindRepositoryRoot();
        var executable = GetExecutable(root);
        var evidenceRoot = CreateEvidenceRoot(root);
        var evidence = await RunFixtureAsync(root, executable, evidenceRoot, "compaction", failureResponder: false);

        evidence.Runtime.Outcome.Should().Be("completed");
        evidence.Runtime.Partial.Should().BeFalse();
        evidence.Runtime.AgentCalls.Should().Be(evidence.Attempts.Count(attempt =>
            attempt.Kind is RecordedRequestKind.Unknown or RecordedRequestKind.MainAgent or RecordedRequestKind.WrapUp));
        evidence.Attempts.Should().NotBeEmpty();
        evidence.Attempts.Should().OnlyContain(attempt => attempt.SessionId == "compaction");
        evidence.Attempts.Select(attempt => attempt.StatusCode).Should().OnlyContain(status => status == 200);

        var requestBodies = evidence.Fake.Requests.Select(bytes => Encoding.UTF8.GetString(bytes)).ToArray();
        requestBodies.Any(body => !HasTools(body)).Should().BeTrue("the native summarizer must send a provider request");
        requestBodies.Any(body => HasTools(body) && body.Contains("Continue working on current task.", StringComparison.Ordinal)).Should().BeTrue();
        requestBodies.Any(body => HasTools(body) && body.Contains("Stop live work for now.", StringComparison.Ordinal)).Should().BeTrue();
        var compactedAttempt = evidence.Attempts.FirstOrDefault(attempt =>
            attempt.EffectiveBody.Length > 0 && !HasTools(Encoding.UTF8.GetString(attempt.EffectiveBody)));
        compactedAttempt.Should().NotBeNull("the native summary request omits the agent tool list");
        Encoding.UTF8.GetString(compactedAttempt!.ResponseBody!).Should().Contain("Synthetic compacted summary");
        evidence.Attempts.Last().Kind.Should().Be(RecordedRequestKind.WrapUp);

        var reportPath = Path.Combine(evidence.SessionRoot, "fixture-report.json");
        File.Exists(reportPath).Should().BeTrue();
        Directory.EnumerateFiles(evidence.SessionRoot, "*", SearchOption.AllDirectories)
            .Should().Contain(path => path.Contains("capture", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".md", StringComparison.OrdinalIgnoreCase));
        IComparisonFixtureEvidence receipts = new ComparisonFixtureEvidence();
        await receipts.WriteAsync(evidence.SessionRoot, executable, "codexplorer", "compaction");
    }

    /// <summary>Verifies pinned runtime non retryable provider failure keeps failure and terminal snapshots.</summary>
    [PinnedCodexplorerFact]
    public async Task PinnedRuntime_NonRetryableProviderFailureKeepsFailureAndTerminalSnapshots()
    {
        var root = FindRepositoryRoot();
        var executable = GetExecutable(root);
        var evidenceRoot = CreateEvidenceRoot(root);
        var evidence = await RunFixtureAsync(root, executable, evidenceRoot, "provider-failure", failureResponder: true);

        evidence.Runtime.Outcome.Should().Be("failed");
        evidence.Runtime.Partial.Should().BeTrue();
        evidence.Runtime.Error.Should().NotBeNullOrWhiteSpace();
        evidence.Attempts.Should().ContainSingle(attempt => attempt.StatusCode == 400 && !attempt.BudgetRejected);
        evidence.Fake.Requests.Should().HaveCount(1, "a provider 400 response is non-retryable");
        var protocolResponses = Path.Combine(evidence.SessionRoot, "state", "protocol-responses.jsonl");
        File.Exists(protocolResponses).Should().BeTrue();
        (await File.ReadAllTextAsync(protocolResponses)).Should().Contain("failed");
        File.Exists(Path.Combine(evidence.SessionRoot, "fixture-report.json")).Should().BeTrue();
        IComparisonFixtureEvidence receipts = new ComparisonFixtureEvidence();
        await receipts.WriteAsync(evidence.SessionRoot, executable, "codexplorer", "provider-failure");
    }

    private static async Task<FixtureResult> RunFixtureAsync(
        string repositoryRoot,
        string executable,
        string evidenceRoot,
        string scenario,
        bool failureResponder)
    {
        var sessionRoot = Path.Combine(evidenceRoot, scenario);
        var checkout = Path.Combine(sessionRoot, "checkout");
        var artifacts = Path.Combine(sessionRoot, "artifacts");
        var stateRoot = Path.Combine(sessionRoot, "state");
        var recordingRoot = Path.Combine(sessionRoot, "recording");
        Directory.CreateDirectory(checkout);
        Directory.CreateDirectory(artifacts);
        await File.WriteAllTextAsync(Path.Combine(checkout, "bulk-a.txt"), BuildLargeSyntheticFile());
        await File.WriteAllTextAsync(Path.Combine(checkout, "bulk-b.txt"), BuildLargeSyntheticFile());
        await File.WriteAllTextAsync(Path.Combine(checkout, "follow-up.txt"), "Synthetic continuation marker.\n");
        await RunGitAsync(checkout, "init", "--quiet");
        await RunGitAsync(checkout, "add", "bulk-a.txt", "bulk-b.txt", "follow-up.txt");
        await RunGitAsync(checkout, "-c", "user.name=Local Fixture", "-c", "user.email=fixture@invalid", "commit", "--quiet", "-m", "synthetic fixture");
        var commit = (await RunGitAsync(checkout, "rev-parse", "HEAD")).Trim();

        using var usageDocument = JsonDocument.Parse("{\"prompt_tokens\":100,\"completion_tokens\":20,\"total_tokens\":120}");
        var usage = usageDocument.RootElement.Clone();
        var mainResponses = 0;
        var fake = failureResponder
            ? new FakeOpenRouterProvider(_ => new FakeCompletion(null, [], usage, "synthetic-provider-error",
                StatusCode: 400,
                RawBody: Encoding.UTF8.GetBytes("{\"error\":{\"message\":\"synthetic non-retryable failure\"}}"),
                ContentType: "application/json"))
            : new FakeOpenRouterProvider(request =>
            {
                if (!HasTools(request))
                    return new FakeCompletion("Synthetic compacted summary with the bulk evidence preserved.", [], usage, "synthetic-compaction-summary");

                var inputTokens = Math.Max(100, request.GetProperty("messages").GetRawText().Length / 4);
                var measuredUsage = JsonSerializer.SerializeToElement(new
                {
                    prompt_tokens = inputTokens, completion_tokens = 20, total_tokens = inputTokens + 20
                });
                var ordinal = Interlocked.Increment(ref mainResponses);
                if (ordinal == 1)
                    return new FakeCompletion(null,
                        [new FakeToolCall("read-bulk-a", "read_file", "{\"path\":\"bulk-a.txt\"}")], measuredUsage, "synthetic-bulk-a-read");
                if (ordinal == 2)
                    return new FakeCompletion(null,
                        [new FakeToolCall("read-bulk-b", "read_file", "{\"path\":\"bulk-b.txt\"}")], measuredUsage, "synthetic-bulk-b-read");
                if (ordinal == 3)
                    return new FakeCompletion(null,
                        [new FakeToolCall("read-follow-up", "read_file", "{\"path\":\"follow-up.txt\"}")], measuredUsage, "synthetic-follow-up-read");
                return new FakeCompletion("Synthetic agent response after local repository inspection.", [], measuredUsage, $"synthetic-agent-{ordinal}");
            });

        var settings = new ComparisonSettings("qwen/qwen3.7-flash", "alibaba", 32768, true)
        {
            ProviderContextTokens = 50_000,
            ProviderMaxCompletionTokens = 32_768,
        };
        await using var proxy = new RecordingProxy(new RecordingProxyOptions(
            RecordingProxyMode.Fake, null, null, fake, recordingRoot, settings));
        await proxy.StartAsync();
        const int maximumCalls = 12;
        const int wrapUpCalls = 1;
        var scope = proxy.BeginScope(new RecordingScopeOptions(scenario, maximumCalls, wrapUpCalls, $"local-{Guid.NewGuid():N}"));
        var prompt = "Inspect bulk-a.txt, bulk-b.txt, and follow-up.txt in the synthetic repository. Use read_file to inspect each file, then report the synthetic markers.";
        var promptHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(prompt)));
        var task = new AutomationTaskDefinition
        {
            TaskId = $"synthetic-{scenario}",
            Title = $"Synthetic {scenario}",
            RepositoryCommit = commit,
            TaskSize = RunnerTaskSize.Small,
            InitialPrompt = prompt,
        };
        var session = new ComparisonSessionPlan(scenario, "synthetic", 1, "codexplorer-treatment", task,
            prompt, promptHash, promptHash, maximumCalls, wrapUpCalls, 50_000);
        var invocation = new RuntimeInvocation(session, executable, checkout, artifacts, stateRoot,
            proxy.Endpoint, scope.Credential, settings, Synthetic: true);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var runtime = new CodexplorerRuntime(scope, new NoOpComparisonHelper());
        var result = await runtime.RunAsync(invocation, timeout.Token);
        await proxy.DrainAsync();
        var attempts = proxy.Attempts;
        var report = JsonSerializer.Serialize(new
        {
            scenario,
            runtime = result,
            attempts,
            fakeRequests = fake.Requests.Select(Encoding.UTF8.GetString).ToArray(),
            repositoryCommit = commit,
            contextWindowTokens = session.ContextWindowTokens,
            executable,
            executableSha256 = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(executable))),
            repositoryRoot,
            liveExecutionStarted = false,
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(sessionRoot, "fixture-report.json"), report);
        return new FixtureResult(result, attempts, fake, sessionRoot);
    }

    private static bool HasTools(JsonElement request) =>
        request.ValueKind == JsonValueKind.Object && request.TryGetProperty("tools", out var tools)
        && tools.ValueKind == JsonValueKind.Array && tools.GetArrayLength() > 0;

    private static bool HasTools(string request)
    {
        try
        {
            using var document = JsonDocument.Parse(request);
            return HasTools(document.RootElement);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string BuildLargeSyntheticFile()
    {
        var line = string.Join(' ', Enumerable.Repeat("synthetic context payload marker", 8));
        return string.Concat(Enumerable.Repeat(line + Environment.NewLine, 600));
    }

    private static async Task<string> RunGitAsync(string workingDirectory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Could not start the local git fixture process.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await stdout;
        var error = await stderr;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Synthetic fixture git command failed: {error}");
        return output;
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(Environment.CurrentDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "TokenGuard.sln")))
                return directory.FullName;
        throw new InvalidOperationException("Could not locate the repository root for the local Codexplorer fixture.");
    }

    private static string CreateEvidenceRoot(string root)
    {
        var path = Path.Combine(root, ".artifacts", "reports", "opencode-comparison", "scratch",
            "codexplorer-runtime-verification", DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ")
                + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string GetExecutable(string root) => Path.Combine(root, "samples", "Codexplorer", "src", "bin", "Release", "net10.0",
        OperatingSystem.IsWindows() ? "Codexplorer.exe" : "Codexplorer");

    private sealed class PinnedCodexplorerFactAttribute : FactAttribute
    {
        /// <summary>Initializes a test that requires the compiled local Codexplorer executable.</summary>
        public PinnedCodexplorerFactAttribute()
        {
            var executable = GetExecutable(FindRepositoryRoot());
            if (!File.Exists(executable))
                this.Skip = "The compiled Codexplorer Release executable is not present in the sample output directory.";
        }
    }

    private sealed class NoOpComparisonHelper : IComparisonHelper
    {
        /// <inheritdoc />
        public Task<string> AnswerAsync(RuntimeInvocation invocation, string question, string? assistantText,
            int callsConsumed, bool wrapUpSent, CancellationToken cancellationToken) =>
            Task.FromResult("Synthetic helper answer.");
    }

    private sealed record FixtureResult(
        RuntimeResult Runtime,
        IReadOnlyList<RecordedAttempt> Attempts,
        FakeOpenRouterProvider Fake,
        string SessionRoot);
}
