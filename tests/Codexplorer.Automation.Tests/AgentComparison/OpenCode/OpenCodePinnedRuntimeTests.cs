using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Codexplorer.Automation.AgentComparison;
using Codexplorer.Automation.AgentComparison.OpenCode;
using Codexplorer.Automation.AgentComparison.Recording;
using Codexplorer.Automation.Configuration;
using FluentAssertions;

namespace Codexplorer.Automation.Tests.AgentComparison.OpenCode;

/// <summary>Verifies the comparison boundary and its observable outcomes.</summary>
public sealed class OpenCodePinnedRuntimeTests
{
    /// <summary>Verifies pinned runtime records compaction denied tools and provider failure against local fakes.</summary>
    [PinnedOpenCodeFact]
    public async Task PinnedRuntime_RecordsCompactionDeniedToolsAndProviderFailureAgainstLocalFakes()
    {
        var root = FindRepositoryRoot();
        var executable = Path.Combine(root, ".artifacts", "reports", "opencode-comparison", "scratch", "tools",
            "opencode-v1.19.3", "opencode");

        var evidenceRoot = Path.Combine(root, ".artifacts", "reports", "opencode-comparison", "scratch",
            "adapter-runtime-verification", DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(evidenceRoot);

        var compaction = await RunFixtureAsync(root, executable, evidenceRoot, "compaction", CompactionScript(), maxAgentCalls: 6);
        var denied = await RunFixtureAsync(root, executable, evidenceRoot, "denied-tools", DeniedToolScript(deniedMarker: Path.Combine(evidenceRoot, "must-not-exist")));
        var failure = await RunFixtureAsync(root, executable, evidenceRoot, "provider-failure", FailureScript());

        var compactEvents = await File.ReadAllLinesAsync(Path.Combine(compaction.ArtifactDirectory,
            "compaction-events.jsonl"));
        var eventTypes = compactEvents.Select(line =>
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.GetProperty("type").GetString();
        }).ToArray();
        eventTypes.Should().Contain("session.compacted", "OpenCode v1.19.3 reports compaction on its legacy event route.");
        eventTypes.Should().NotContain("session.next.compaction.started").And.NotContain("session.next.compaction.ended");
        var compactionExportPath = compaction.Artifacts.Single(path => path.EndsWith("-export.json", StringComparison.Ordinal));
        using var compactionExport = JsonDocument.Parse(await File.ReadAllTextAsync(compactionExportPath));
        var compactionMessages = EnumerateObjects(compactionExport.RootElement)
            .Where(message => message.TryGetProperty("info", out var info)
                && info.ValueKind == JsonValueKind.Object
                && info.TryGetProperty("agent", out var agent)
                && agent.GetString() == "compaction")
            .ToArray();
        compactionMessages.Should().NotBeEmpty("the pinned server must export its actual compaction message");
        compactionMessages.SelectMany(ReadTextParts).Should().Contain("Synthetic compacted summary.",
            "the fake provider's known summary must survive OpenCode's compaction and session export");
        using var resolvedPromptDocument = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(compaction.ArtifactDirectory, "compaction-resolved-prompt.json")));
        resolvedPromptDocument.RootElement.GetProperty("templatePrompt").GetString().Should().Contain("${ARTIFACTS_DIR}");
        var resolvedPrompt = resolvedPromptDocument.RootElement.GetProperty("resolvedPrompt").GetString();
        resolvedPrompt.Should().Contain(compaction.ArtifactDirectory).And.NotContain("${ARTIFACTS_DIR}");
        compaction.Attempts.Should().OnlyContain(attempt => attempt.Kind != RecordedRequestKind.Compaction);
        AssertBudgetBound(compaction.Attempts, 6);
        compaction.Artifacts.Should().Contain(path => path.EndsWith("-export.json", StringComparison.Ordinal));

        denied.Runtime.Outcome.Should().Be("completed");
        denied.Runtime.Partial.Should().BeFalse();
        File.Exists(Path.Combine(evidenceRoot, "must-not-exist")).Should().BeFalse();
        denied.Attempts.Should().NotBeEmpty();
        AssertBudgetBound(denied.Attempts, 3);
        var deniedResponses = await File.ReadAllLinesAsync(Path.Combine(denied.ArtifactDirectory, "denied-tools-prompt-responses.jsonl"));
        var abortRecords = deniedResponses.Select(line =>
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.TryGetProperty("operation", out var operation)
                && operation.GetString() == "abort-session"
                && document.RootElement.GetProperty("statusCode").GetInt32() == 200
                && document.RootElement.GetProperty("body").GetString() == "true";
        }).ToArray();
        abortRecords.Should().Contain(true);
        var deniedEvents = await File.ReadAllLinesAsync(Path.Combine(denied.ArtifactDirectory, "denied-tools-events.jsonl"));
        deniedEvents.Should().NotContain(line => line.Contains("Transport error", StringComparison.OrdinalIgnoreCase),
            "native web tools must be denied before any network request is attempted");
        var deniedToolEvents = deniedEvents.SelectMany(ReadToolEvents).ToArray();
        AssertDeniedCall(deniedToolEvents, "deny-bash");
        AssertDeniedCall(deniedToolEvents, "deny-webfetch");
        AssertDeniedCall(deniedToolEvents, "deny-task");
        AssertDeniedCall(deniedToolEvents, "deny-external-write");
        AssertDeniedCall(deniedToolEvents, "deny-bash-after-cap");
        deniedToolEvents.Should().NotContain(toolEvent => toolEvent.Tool == "websearch" && toolEvent.Status == "completed",
            "an unknown or denied websearch request must never complete as a native tool execution");
        deniedToolEvents.Where(toolEvent => toolEvent.CallId == "deny-bash-after-cap")
            .Where(toolEvent => toolEvent.Status is "error" or "completed")
            .Select(toolEvent => toolEvent.Status).Should().Equal("error");

        failure.Runtime.Outcome.Should().Be("failed");
        failure.Runtime.Partial.Should().BeTrue();
        failure.Runtime.Error.Should().NotBeNullOrWhiteSpace();
        AssertBudgetBound(failure.Attempts, 3);
        failure.Artifacts.Should().Contain(path => path.EndsWith("-process.log", StringComparison.Ordinal));
        failure.Artifacts.Should().Contain(path => path.EndsWith("-export-error.json", StringComparison.Ordinal)
            || path.EndsWith("-export.json", StringComparison.Ordinal));
        IComparisonFixtureEvidence receipts = new ComparisonFixtureEvidence();
        foreach (var scenario in new[] { "compaction", "denied-tools", "provider-failure" })
            await receipts.WriteAsync(Path.Combine(evidenceRoot, scenario), executable, "opencode", scenario);
    }

    private static async Task<FixtureResult> RunFixtureAsync(
        string repositoryRoot,
        string executable,
        string evidenceRoot,
        string scenario,
        IReadOnlyList<FakeCompletion> script,
        int maxAgentCalls = 3)
    {
        var sessionRoot = Path.Combine(evidenceRoot, scenario);
        var checkout = Path.Combine(sessionRoot, "checkout");
        var artifacts = Path.Combine(sessionRoot, "artifacts");
        var stateRoot = Path.Combine(sessionRoot, "state");
        var recordingRoot = Path.Combine(sessionRoot, "recording");
        Directory.CreateDirectory(checkout);
        Directory.CreateDirectory(artifacts);
        await File.WriteAllTextAsync(Path.Combine(checkout, "README.md"), "Local OpenCode fixture.\n");
        await File.WriteAllTextAsync(Path.Combine(sessionRoot, "AGENTS.md"), "PARENT_INSTRUCTION_SENTINEL must not be loaded.\n");
        await File.WriteAllTextAsync(Path.Combine(sessionRoot, "opencode.json"), """
            {"username":"PARENT_CONFIG_SENTINEL","instructions":["PARENT_INSTRUCTION_SENTINEL.md"]}
            """);
        var gitHome = Path.Combine(sessionRoot, "git-home");
        var sourceSha = await InitializeCheckoutAsync(checkout, gitHome);

        using var usageDocument = JsonDocument.Parse("""
            {"prompt_tokens":100,"completion_tokens":10,"total_tokens":110}
            """);
        var fake = new FakeOpenRouterProvider(script);
        var settings = new ComparisonSettings("qwen/qwen3.7-flash", "openrouter", 32768, true)
        {
            ProviderContextTokens = 1_000_000,
            ProviderMaxCompletionTokens = 32_768,
        };
        await using var proxy = new RecordingProxy(new RecordingProxyOptions(
            RecordingProxyMode.Fake,
            null,
            null,
            fake,
            recordingRoot,
            settings));
        await proxy.StartAsync();
        var scope = proxy.BeginScope(new RecordingScopeOptions(scenario, maxAgentCalls, 1, $"local-{Guid.NewGuid():N}"));
        var task = new AutomationTaskDefinition
        {
            TaskId = $"synthetic-{scenario}",
            Title = $"Synthetic {scenario}",
            RepositoryCommit = sourceSha,
            TaskSize = RunnerTaskSize.Small,
            InitialPrompt = "Use only the local repository and report that the synthetic task was handled. Save evidence at ${ARTIFACTS_DIR}/evidence.md.",
        };
        var promptHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(task.InitialPrompt)));
        var session = new ComparisonSessionPlan(scenario, "synthetic", 1, "opencode", task, task.InitialPrompt,
            promptHash, promptHash, maxAgentCalls, 1, null);
        var invocation = new RuntimeInvocation(session, executable, checkout, artifacts, stateRoot, proxy.Endpoint,
            scope.Credential, settings, Synthetic: true);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var runtime = new OpenCodeRuntime(scope, new NoOpComparisonHelper());
        var result = await runtime.RunAsync(invocation, timeout.Token);
        var captures = result.CapturePaths.Select(path => Path.Combine(artifacts, path.Replace('/', Path.DirectorySeparatorChar))).ToArray();
        var existingCaptures = captures.Where(File.Exists).ToArray();
        var attempts = proxy.Attempts;
        var environmentInventory = await File.ReadAllTextAsync(Path.Combine(artifacts, $"{scenario}-environment.json"));
        var effectiveConfiguration = await File.ReadAllTextAsync(Path.Combine(artifacts, $"{scenario}-effective-config.json"));
        environmentInventory.Should().NotContain(Path.Combine(sessionRoot, "AGENTS.md"));
        environmentInventory.Should().NotContain(Path.Combine(sessionRoot, "opencode.json"));
        effectiveConfiguration.Should().NotContain("PARENT_CONFIG_SENTINEL");
        fake.Requests.Select(bytes => Encoding.UTF8.GetString(bytes)).Should().OnlyContain(
            request => !request.Contains("PARENT_INSTRUCTION_SENTINEL", StringComparison.Ordinal));
        var report = JsonSerializer.Serialize(new
        {
            scenario,
            runtime = result,
            attempts,
            fakeRequests = fake.Requests.Select(bytes => Encoding.UTF8.GetString(bytes)).ToArray(),
            captureFiles = existingCaptures.Select(path => Path.GetRelativePath(sessionRoot, path)).ToArray(),
            executable,
            executableSha256 = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(executable))),
            repositoryRoot,
            sourceSha,
            parentCandidates = new[]
            {
                new { path = "../AGENTS.md", sha256 = await HashFileAsync(Path.Combine(sessionRoot, "AGENTS.md")) },
                new { path = "../opencode.json", sha256 = await HashFileAsync(Path.Combine(sessionRoot, "opencode.json")) },
            },
            liveExecutionStarted = false,
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(sessionRoot, "fixture-report.json"), report);
        return new FixtureResult(result, attempts, existingCaptures, artifacts);
    }

    private static IReadOnlyList<FakeCompletion> CompactionScript()
    {
        using var highUsageDocument = JsonDocument.Parse("""
            {"prompt_tokens":1000001,"completion_tokens":20,"total_tokens":1000021}
            """);
        using var normalUsageDocument = JsonDocument.Parse("""
            {"prompt_tokens":100,"completion_tokens":10,"total_tokens":110}
            """);
        return
        [
            new FakeCompletion("Initial synthetic response.", [], highUsageDocument.RootElement.Clone(), "compaction-trigger"),
            new FakeCompletion("Synthetic compacted summary.", [], normalUsageDocument.RootElement.Clone(), "compaction-summary"),
            new FakeCompletion("Synthetic continued response.", [], normalUsageDocument.RootElement.Clone(), "compaction-continuation"),
            new FakeCompletion("Synthetic continued response.", [], normalUsageDocument.RootElement.Clone(), "compaction-continuation-2"),
            new FakeCompletion("Synthetic continued response.", [], normalUsageDocument.RootElement.Clone(), "compaction-continuation-3"),
            new FakeCompletion("Synthetic wrap-up response.", [], normalUsageDocument.RootElement.Clone(), "compaction-wrap-up"),
        ];
    }

    private static IReadOnlyList<FakeCompletion> DeniedToolScript(string deniedMarker)
    {
        using var usageDocument = JsonDocument.Parse("""
            {"prompt_tokens":100,"completion_tokens":10,"total_tokens":110}
            """);
        var usage = usageDocument.RootElement.Clone();
        return
        [
            new FakeCompletion(null,
            [
                new FakeToolCall("deny-bash", "bash", JsonSerializer.Serialize(new { command = $"touch '{deniedMarker}'" })),
                new FakeToolCall("deny-webfetch", "webfetch", JsonSerializer.Serialize(new { url = "http://127.0.0.1:1/denied" })),
                new FakeToolCall("deny-websearch", "websearch", JsonSerializer.Serialize(new { query = "synthetic denied query" })),
                new FakeToolCall("deny-task", "task", JsonSerializer.Serialize(new { description = "must not delegate", prompt = "must not run" })),
                new FakeToolCall("deny-external-write", "write", JsonSerializer.Serialize(new
                {
                    filePath = deniedMarker,
                    content = "external write must be denied",
                })),
            ], usage, "denied-tools"),
            new FakeCompletion(null,
            [new FakeToolCall("deny-bash-after-cap", "bash", JsonSerializer.Serialize(new { command = "echo must-not-run" }))],
            usage, "denied-tools-after-cap"),
            new FakeCompletion("Synthetic denied tools wrap-up.", [], usage, "denied-tools-wrap-up"),
        ];
    }

    private static IReadOnlyList<FakeCompletion> FailureScript()
    {
        using var usageDocument = JsonDocument.Parse("{\"prompt_tokens\":10,\"completion_tokens\":0}");
        var error = Encoding.UTF8.GetBytes("{\"error\":{\"message\":\"synthetic upstream failure\"}}");
        return Enumerable.Range(0, 4)
            .Select(index => new FakeCompletion(null, [], usageDocument.RootElement.Clone(), $"failure-{index}",
                StatusCode: 400, RawBody: error, ContentType: "application/json"))
            .ToArray();
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(Environment.CurrentDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "TokenGuard.sln")))
                return directory.FullName;
        throw new InvalidOperationException("Could not locate the TokenGuard repository root for the local OpenCode fixture.");
    }

    private static async Task<string> InitializeCheckoutAsync(string checkout, string home)
    {
        Directory.CreateDirectory(home);
        var globalConfig = Path.Combine(home, "empty.gitconfig");
        await File.WriteAllTextAsync(globalConfig, string.Empty);
        await RunGitAsync(checkout, home, globalConfig, ["init", "-q"]);
        await RunGitAsync(checkout, home, globalConfig, ["config", "--local", "user.name", "TokenGuard fixture"]);
        await RunGitAsync(checkout, home, globalConfig, ["config", "--local", "user.email", "fixture@example.invalid"]);
        await RunGitAsync(checkout, home, globalConfig, ["add", "README.md"]);
        await RunGitAsync(checkout, home, globalConfig, ["commit", "--quiet", "-m", "Synthetic fixture"]);
        return (await RunGitAsync(checkout, home, globalConfig, ["rev-parse", "HEAD"])).Trim();
    }

    private static async Task<string> RunGitAsync(
        string checkout,
        string home,
        string globalConfig,
        IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = checkout,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        startInfo.Environment.Clear();
        startInfo.Environment["HOME"] = home;
        startInfo.Environment["PATH"] = "/usr/bin:/bin:/usr/sbin:/sbin";
        startInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        startInfo.Environment["GIT_CONFIG_GLOBAL"] = globalConfig;
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start git for the local OpenCode fixture.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Git fixture command failed: {error}");
        return output;
    }

    private static async Task<string> HashFileAsync(string path)
    {
        return Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(path)));
    }

    private static void AssertBudgetBound(IReadOnlyList<RecordedAttempt> attempts, int totalLimit)
    {
        attempts.Count(attempt => !attempt.BudgetRejected).Should().BeLessThanOrEqualTo(totalLimit);
        attempts.Where(attempt => attempt.BudgetRejected).Should().OnlyContain(attempt => attempt.StatusCode == 429);
    }

    private static IEnumerable<NativeToolEvent> ReadToolEvents(string line)
    {
        using var document = JsonDocument.Parse(line);
        if (!document.RootElement.TryGetProperty("properties", out var properties)
            || !properties.TryGetProperty("part", out var part)
            || !part.TryGetProperty("type", out var type)
            || type.GetString() != "tool"
            || !part.TryGetProperty("state", out var state)
            || !state.TryGetProperty("status", out var status)
            || !part.TryGetProperty("tool", out var tool))
            return [];
        var callId = part.TryGetProperty("callID", out var callIdElement)
            ? callIdElement.GetString() ?? string.Empty
            : string.Empty;
        var error = state.TryGetProperty("error", out var errorElement)
            ? errorElement.ToString()
            : string.Empty;
        return [new NativeToolEvent(tool.GetString() ?? string.Empty, callId, status.GetString() ?? string.Empty, error)];
    }

    private static void AssertDeniedCall(IReadOnlyList<NativeToolEvent> events, string callId)
    {
        var matching = events.Where(toolEvent => toolEvent.CallId == callId
            && toolEvent.Status is "error" or "completed").ToArray();
        matching.Should().ContainSingle($"OpenCode must produce one terminal event for denied tool call {callId}");
        matching[0].Status.Should().Be("error", $"OpenCode must reject denied tool call {callId}");
        matching[0].Error.Contains("Transport error", StringComparison.OrdinalIgnoreCase)
            .Should().BeFalse($"denied tool call {callId} must not execute and fail later during I/O");
        (matching[0].Error.Contains("permission", StringComparison.OrdinalIgnoreCase)
            || matching[0].Error.Contains("denied", StringComparison.OrdinalIgnoreCase)
            || matching[0].Error.Contains("unavailable tool", StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue($"denied tool call {callId} must fail before execution, got: {matching[0].Error}");
    }

    private static IEnumerable<JsonElement> EnumerateObjects(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            yield return element;
            foreach (var property in element.EnumerateObject())
            foreach (var nested in EnumerateObjects(property.Value))
                yield return nested;
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            foreach (var nested in EnumerateObjects(item))
                yield return nested;
        }
    }

    private static IEnumerable<string> ReadTextParts(JsonElement message)
    {
        if (!message.TryGetProperty("parts", out var parts) || parts.ValueKind != JsonValueKind.Array)
            return [];
        return parts.EnumerateArray()
            .Where(part => part.ValueKind == JsonValueKind.Object
                && part.TryGetProperty("type", out var type)
                && type.GetString() == "text"
                && part.TryGetProperty("text", out _))
            .Select(part => part.GetProperty("text").GetString() ?? string.Empty);
    }

    private sealed class PinnedOpenCodeFactAttribute : FactAttribute
    {
        /// <summary>Initializes a test that requires the pinned local OpenCode executable.</summary>
        public PinnedOpenCodeFactAttribute()
        {
            var root = FindRepositoryRoot();
            var executable = Path.Combine(root, ".artifacts", "reports", "opencode-comparison", "scratch", "tools",
                "opencode-v1.19.3", "opencode");
            if (!File.Exists(executable))
                this.Skip = "The pinned OpenCode v1.19.3 binary is not installed in the local scratch tools directory.";
        }
    }

    private sealed class NoOpComparisonHelper : IComparisonHelper
    {
        /// <inheritdoc />
        public Task<string> AnswerAsync(RuntimeInvocation invocation, string question, string? assistantText, int callsConsumed,
            bool wrapUpSent, CancellationToken cancellationToken) => Task.FromResult("Synthetic helper answer.");
    }

    private sealed record FixtureResult(
        RuntimeResult Runtime,
        IReadOnlyList<RecordedAttempt> Attempts,
        IReadOnlyList<string> Artifacts,
        string ArtifactDirectory);

    private sealed record NativeToolEvent(string Tool, string CallId, string Status, string Error);
}
