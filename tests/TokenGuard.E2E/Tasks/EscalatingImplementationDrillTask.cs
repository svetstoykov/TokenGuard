using System.Text.Json;
using FluentAssertions;

namespace TokenGuard.E2E.Tasks;

/// <summary>
/// Defines a heavy mixed coding, review, and CI triage scenario designed to accumulate tokens in staged waves.
/// </summary>
internal static class EscalatingImplementationDrillTask
{
    private const string CompletionMarker = "ESCALATING_IMPLEMENTATION_DRILL_COMPLETE";

    /// <summary>
    /// Creates the heavy staged task definition consumed by the shared E2E loop.
    /// </summary>
    public static AgentLoopTaskDefinition Create() => new(
        name: "EscalatingImplementationDrill",
        conversationName: "e2e-escalating-implementation-drill",
        systemPrompt:
            "You are a senior engineering agent running inside a TokenGuard E2E test. " +
            "Your job is to execute a heavy, staged implementation drill that mixes coding, code review, and CI triage. " +
            "You MUST use the provided tools for every file operation and must use the transcript-expansion tool instead of inventing long dialogue yourself. " +
            "Read all control files before writing outputs. Re-read your own outputs after each major write. " +
            "When all required artefacts are complete, respond with exactly three bullet points. " +
            $"The final bullet must be '{CompletionMarker}'.",
        userMessage:
            "Task: complete the escalating implementation drill in staged token ramps.\n" +
            "Step 1 - list all files, then read task-brief.txt and execution-plan.txt.\n" +
            "  Also read output-manifest.txt before creating or editing any final artefact.\n" +
            "Step 2 - expand all transcript recipes using expand_transcript_stage into these files exactly:\n" +
            "  - generated/stage-01-transcript.md from recipes/stage-01.json\n" +
            "  - generated/stage-02-transcript.md from recipes/stage-02.json\n" +
            "  - generated/stage-03-transcript.md from recipes/stage-03.json\n" +
            "  - generated/stage-04-transcript.md from recipes/stage-04.json\n" +
            "Step 3 - read all generated transcripts plus all source, test, CI, and review input files.\n" +
            "Step 4 - overwrite the existing root file 'implementation-plan.md' with sections: Stage 1, Stage 2, Stage 3, Stage 4, and Final Fix Strategy. Each stage section must reference the token target and the main failure themes found there.\n" +
            "Step 5 - overwrite the existing root file 'review-reconciliation.md' listing contradictions between reviewer comments, current code, tests, and CI logs. Include at least eight numbered contradictions.\n" +
            "Step 6 - overwrite the existing root file 'ci-triage.md' with sections for failing suites, probable root causes, confidence, and next verification actions.\n" +
            "Step 7 - overwrite the existing root file 'fix-forward.patch.md' so it proposes coordinated code changes across the seeded source files. Include headings for each file and concrete patch intent.\n" +
            "Step 8 - overwrite the existing root file 'token-ramp-report.json' with stageTargets array, expandedTranscriptFiles array, contradictionCount, and recommendedExecutionOrder array.\n" +
            "Step 9 - read back these exact files to verify they are populated and consistent: implementation-plan.md, review-reconciliation.md, ci-triage.md, fix-forward.patch.md, token-ramp-report.json, generated/stage-01-transcript.md, generated/stage-02-transcript.md, generated/stage-03-transcript.md, generated/stage-04-transcript.md.\n" +
            "Do not claim completion until every generated transcript exists and all five final artefacts are written and verified.",
        completionMarker: CompletionMarker,
        seedWorkspaceAsync: SeedAsync,
        assertOutcomeAsync: AssertAsync);

    private static async Task SeedAsync(string dir)
    {
        Directory.CreateDirectory(Path.Combine(dir, "recipes"));
        Directory.CreateDirectory(Path.Combine(dir, "generated"));
        Directory.CreateDirectory(Path.Combine(dir, "src"));
        Directory.CreateDirectory(Path.Combine(dir, "tests"));
        Directory.CreateDirectory(Path.Combine(dir, "ci"));
        Directory.CreateDirectory(Path.Combine(dir, "reviews"));
        Directory.CreateDirectory(Path.Combine(dir, "docs"));

        await File.WriteAllTextAsync(Path.Combine(dir, "task-brief.txt"),
            "Escalating Implementation Drill\n" +
            "==============================\n\n" +
            "Goal:\n" +
            "Produce a heavy mixed coding/review/CI triage response that forces repeated inspection and synthesis.\n\n" +
            "Rules:\n" +
            "- Use tools for every file read and write.\n" +
            "- Use expand_transcript_stage for all stage transcript generation.\n" +
            "- Treat stage targets as cumulative token ramps: 2k, 6k, 7k, 20k.\n" +
            "- Prefer synthesis artefacts over short conversational claims.\n" +
            "- Re-read every output file after writing it.\n\n" +
            "Core problem:\n" +
            "The orchestrator pipeline recently gained retry and streaming support, but CI now fails under multi-stage load and reviewers disagree on whether the bug is state leakage, invalid retry semantics, or brittle test assumptions.\n");

        await File.WriteAllTextAsync(Path.Combine(dir, "execution-plan.txt"),
            "Execution Plan\n" +
            "==============\n\n" +
            "1. Expand recipe files into large transcript stages.\n" +
            "2. Read generated stages in order because later stages refer back to unresolved contradictions.\n" +
            "3. Cross-check source files, failing tests, CI logs, architecture notes, and reviewer comments.\n" +
            "4. Produce artefacts that preserve disagreement instead of collapsing it too early.\n" +
            "5. Recommend a coordinated execution order for fixes and verification.\n");

        await File.WriteAllTextAsync(Path.Combine(dir, "output-manifest.txt"),
            "Output Manifest\n" +
            "===============\n\n" +
            "Final artefact locations are exact and already scaffolded at workspace root.\n" +
            "Do not create alternate copies in generated/, docs/, reviews/, or nested folders.\n\n" +
            "Overwrite these existing root files with final content:\n" +
            "- implementation-plan.md\n" +
            "- review-reconciliation.md\n" +
            "- ci-triage.md\n" +
            "- fix-forward.patch.md\n" +
            "- token-ramp-report.json\n\n" +
            "Generated transcript locations are exact:\n" +
            "- generated/stage-01-transcript.md\n" +
            "- generated/stage-02-transcript.md\n" +
            "- generated/stage-03-transcript.md\n" +
            "- generated/stage-04-transcript.md\n\n" +
            "Verification rule:\n" +
            "Read back every final artefact after writing it. Completion is invalid unless each exact path above exists and is populated.\n");

        await File.WriteAllTextAsync(Path.Combine(dir, "implementation-plan.md"),
            "PLACEHOLDER: overwrite this root file with the final implementation plan.\n");
        await File.WriteAllTextAsync(Path.Combine(dir, "review-reconciliation.md"),
            "PLACEHOLDER: overwrite this root file with the final review reconciliation.\n");
        await File.WriteAllTextAsync(Path.Combine(dir, "ci-triage.md"),
            "PLACEHOLDER: overwrite this root file with the final CI triage analysis.\n");
        await File.WriteAllTextAsync(Path.Combine(dir, "fix-forward.patch.md"),
            "PLACEHOLDER: overwrite this root file with the final fix-forward patch intent.\n");
        await File.WriteAllTextAsync(Path.Combine(dir, "token-ramp-report.json"),
            "{\n  \"placeholder\": true\n}\n");

        await WriteRecipeAsync(dir, "stage-01.json", "01", "Initial Failure Surfacing", "Stabilize understanding of current defect and early reviewer disagreement.", "baseline drift", "2k", 18, 4,
            ["src/Coordinator.cs", "src/RetryPolicy.cs", "tests/CoordinatorTests.cs"],
            [
                "Early stage should feel like exploratory coding discussion with repeated reopen-and-verify loops.",
                "Team disagreement starts small but does not resolve in first pass.",
                "This stage should be enough to push a modest context window segment.",
            ]);
        await WriteRecipeAsync(dir, "stage-02.json", "02", "Review Pressure And Scope Expansion", "Incorporate broader reviewer objections and hidden dependency boundaries.", "scope inflation", "6k", 34, 5,
            ["src/StreamingSession.cs", "src/Coordinator.cs", "reviews/reviewer-thread-a.md", "tests/StreamingSessionTests.cs"],
            [
                "Second stage should feel like back-and-forth code review where every answer triggers another check.",
                "More downstream invariants should appear than stage one captured.",
                "This stage should materially widen blast radius discussion.",
            ]);
        await WriteRecipeAsync(dir, "stage-03.json", "03", "CI Triage And Regression Mapping", "Correlate CI evidence with the conflicting code review advice.", "evidence explosion", "7k", 40, 6,
            ["ci/build-1842.log", "ci/test-failures.log", "src/Coordinator.cs", "tests/RetryFlowTests.cs"],
            [
                "Third stage should repeatedly tie error signatures back to code and tests.",
                "Agent should sound like it must keep re-reading evidence before converging.",
                "This stage should create dense evidence synthesis pressure.",
            ]);
        await WriteRecipeAsync(dir, "stage-04.json", "04", "Fix-Forward Planning Under Load", "Force final reconciliation across code, review, architecture, and CI triage.", "decision overload", "20k", 75, 7,
            ["docs/architecture-notes.md", "reviews/reviewer-thread-b.md", "src/Coordinator.cs", "src/RetryPolicy.cs", "src/StreamingSession.cs", "tests/IntegrationFlowTests.cs"],
            [
                "Final stage should be very heavy and explicitly cumulative.",
                "Every cycle should preserve unresolved contradictions until later verification closes them.",
                "Back-and-forth should feel like an implementation agent getting pulled into more checks every round.",
            ]);

        await File.WriteAllTextAsync(Path.Combine(dir, "src", "Coordinator.cs"),
            "using System.Collections.Concurrent;\n\n" +
            "namespace Sample.Pipeline;\n\n" +
            "public sealed class Coordinator\n" +
            "{\n" +
            "    private readonly RetryPolicy retryPolicy = new();\n" +
            "    private readonly ConcurrentDictionary<string, int> attempts = new();\n\n" +
            "    public async Task<string> ExecuteAsync(string sessionId, Func<Task<string>> action)\n" +
            "    {\n" +
            "        attempts.AddOrUpdate(sessionId, 1, static (_, current) => current + 1);\n" +
            "\n" +
            "        if (attempts[sessionId] > 1)\n" +
            "        {\n" +
            "            sessionId = sessionId + \"-retry\";\n" +
            "        }\n" +
            "\n" +
            "        try\n" +
            "        {\n" +
            "            return await retryPolicy.ExecuteAsync(action);\n" +
            "        }\n" +
            "        catch\n" +
            "        {\n" +
            "            return $\"FAILED:{sessionId}\";\n" +
            "        }\n" +
            "    }\n" +
            "}\n");

        await File.WriteAllTextAsync(Path.Combine(dir, "src", "RetryPolicy.cs"),
            "namespace Sample.Pipeline;\n\n" +
            "public sealed class RetryPolicy\n" +
            "{\n" +
            "    public async Task<string> ExecuteAsync(Func<Task<string>> action)\n" +
            "    {\n" +
            "        try\n" +
            "        {\n" +
            "            return await action();\n" +
            "        }\n" +
            "        catch\n" +
            "        {\n" +
            "            return await action();\n" +
            "        }\n" +
            "    }\n" +
            "}\n");

        await File.WriteAllTextAsync(Path.Combine(dir, "src", "StreamingSession.cs"),
            "namespace Sample.Pipeline;\n\n" +
            "public sealed class StreamingSession\n" +
            "{\n" +
            "    private readonly List<string> frames = [];\n\n" +
            "    public IReadOnlyList<string> Frames => frames;\n\n" +
            "    public void Record(string chunk)\n" +
            "    {\n" +
            "        frames.Add(chunk);\n" +
            "        if (frames.Count > 3)\n" +
            "        {\n" +
            "            frames.Clear();\n" +
            "        }\n" +
            "    }\n" +
            "}\n");

        await File.WriteAllTextAsync(Path.Combine(dir, "tests", "CoordinatorTests.cs"),
            "using Xunit;\n\n" +
            "namespace Sample.Pipeline.Tests;\n\n" +
            "public sealed class CoordinatorTests\n" +
            "{\n" +
            "    [Fact]\n" +
            "    public async Task ExecuteAsync_WhenRetryHappens_PreservesOriginalSessionId()\n" +
            "    {\n" +
            "        var coordinator = new Coordinator();\n" +
            "        var attempts = 0;\n" +
            "\n" +
            "        var result = await coordinator.ExecuteAsync(\"session-42\", async () =>\n" +
            "        {\n" +
            "            attempts++;\n" +
            "            if (attempts == 1)\n" +
            "            {\n" +
            "                throw new InvalidOperationException(\"boom\");\n" +
            "            }\n" +
            "\n" +
            "            return \"ok\";\n" +
            "        });\n" +
            "\n" +
            "        Assert.Equal(\"ok\", result);\n" +
            "    }\n" +
            "}\n");

        await File.WriteAllTextAsync(Path.Combine(dir, "tests", "StreamingSessionTests.cs"),
            "using Xunit;\n\n" +
            "namespace Sample.Pipeline.Tests;\n\n" +
            "public sealed class StreamingSessionTests\n" +
            "{\n" +
            "    [Fact]\n" +
            "    public void Record_WhenMoreThanThreeFrames_StillKeepsRecentFrames()\n" +
            "    {\n" +
            "        var session = new StreamingSession();\n" +
            "        session.Record(\"a\");\n" +
            "        session.Record(\"b\");\n" +
            "        session.Record(\"c\");\n" +
            "        session.Record(\"d\");\n" +
            "\n" +
            "        Assert.Equal(3, session.Frames.Count);\n" +
            "    }\n" +
            "}\n");

        await File.WriteAllTextAsync(Path.Combine(dir, "tests", "RetryFlowTests.cs"),
            "using Xunit;\n\n" +
            "namespace Sample.Pipeline.Tests;\n\n" +
            "public sealed class RetryFlowTests\n" +
            "{\n" +
            "    [Fact]\n" +
            "    public async Task ExecuteAsync_WhenActionFailsTwice_StopsAfterConfiguredRetries()\n" +
            "    {\n" +
            "        var policy = new RetryPolicy();\n" +
            "        var attempts = 0;\n" +
            "\n" +
            "        await Assert.ThrowsAsync<InvalidOperationException>(async () =>\n" +
            "            await policy.ExecuteAsync(async () =>\n" +
            "            {\n" +
            "                attempts++;\n" +
            "                throw new InvalidOperationException($\"boom-{attempts}\");\n" +
            "            }));\n" +
            "    }\n" +
            "}\n");

        await File.WriteAllTextAsync(Path.Combine(dir, "tests", "IntegrationFlowTests.cs"),
            "using Xunit;\n\n" +
            "namespace Sample.Pipeline.Tests;\n\n" +
            "public sealed class IntegrationFlowTests\n" +
            "{\n" +
            "    [Fact]\n" +
            "    public async Task EndToEnd_WhenRetryAndStreamingCombine_ProducesStableResult()\n" +
            "    {\n" +
            "        var coordinator = new Coordinator();\n" +
            "        var session = new StreamingSession();\n" +
            "        session.Record(\"first\");\n" +
            "        session.Record(\"second\");\n" +
            "        session.Record(\"third\");\n" +
            "        session.Record(\"fourth\");\n" +
            "\n" +
            "        var result = await coordinator.ExecuteAsync(\"integration-7\", async () =>\n" +
            "        {\n" +
            "            await Task.Delay(1);\n" +
            "            return string.Join(\",\", session.Frames);\n" +
            "        });\n" +
            "\n" +
            "        Assert.Equal(\"second,third,fourth\", result);\n" +
            "    }\n" +
            "}\n");

        await File.WriteAllTextAsync(Path.Combine(dir, "ci", "build-1842.log"),
            string.Join("\n", Enumerable.Range(1, 80).Select(i =>
                $"[build-1842] step {i:000}: pipeline validation {(i % 9 == 0 ? "FAILED: retry metadata mismatch detected" : "ok")} during shard {(i % 6) + 1}.")) +
            "\nSummary: build produced flaky pass/fail split across retry-enabled shards.\n");

        await File.WriteAllTextAsync(Path.Combine(dir, "ci", "test-failures.log"),
            string.Join("\n", Enumerable.Range(1, 90).Select(i =>
                $"[test-run] case {i:000}: {(i % 11 == 0 ? "CoordinatorTests.ExecuteAsync_WhenRetryHappens_PreservesOriginalSessionId FAILED" : i % 7 == 0 ? "IntegrationFlowTests.EndToEnd_WhenRetryAndStreamingCombine_ProducesStableResult FAILED" : "pass")} | note {(i % 5) + 1}: evidence cluster {i % 13}.")) +
            "\nPrimary signatures: mutated session ids, dropped stream frames, swallowed terminal failures.\n");

        await File.WriteAllTextAsync(Path.Combine(dir, "reviews", "reviewer-thread-a.md"),
            "# Reviewer Thread A\n\n" +
            string.Join("\n\n", Enumerable.Range(1, 30).Select(i =>
                $"Comment {i}: Coordinator should not mutate externally visible session identifiers during retries, but current code appears to rewrite them after attempt counting. Please prove whether tests are wrong or implementation is wrong before adjusting anything.")));

        await File.WriteAllTextAsync(Path.Combine(dir, "reviews", "reviewer-thread-b.md"),
            "# Reviewer Thread B\n\n" +
            string.Join("\n\n", Enumerable.Range(1, 34).Select(i =>
                $"Comment {i}: StreamingSession clearing strategy looks incompatible with integration expectations. However, if frames are retained indefinitely, memory growth and stale context risks return. Explain exact retention contract before changing buffer logic.")));

        await File.WriteAllTextAsync(Path.Combine(dir, "docs", "architecture-notes.md"),
            "# Architecture Notes\n\n" +
            "Retry subsystem contract:\n" +
            "- Preserve caller-visible identifiers across retries.\n" +
            "- Surface terminal exception after retry budget is exhausted.\n" +
            "- Keep retry metadata internal to telemetry only.\n\n" +
            "Streaming subsystem contract:\n" +
            "- Retain recent frames required for downstream assembly.\n" +
            "- Trim oldest frames instead of clearing entire buffer.\n" +
            "- Never erase all prior evidence merely because a new frame arrives.\n\n" +
            string.Join("\n", Enumerable.Range(1, 40).Select(i =>
                $"Note {i}: integration path {i} depends on stable identifiers, bounded retention, and explicit failure propagation.")));
    }

    private static async Task AssertAsync(string dir, string? finalText)
    {
        var implementationPlan = await File.ReadAllTextAsync(Path.Combine(dir, "implementation-plan.md"));
        var reviewReconciliation = await File.ReadAllTextAsync(Path.Combine(dir, "review-reconciliation.md"));
        var ciTriage = await File.ReadAllTextAsync(Path.Combine(dir, "ci-triage.md"));
        var fixForward = await File.ReadAllTextAsync(Path.Combine(dir, "fix-forward.patch.md"));
        var tokenRampReport = await File.ReadAllTextAsync(Path.Combine(dir, "token-ramp-report.json"));
        var stage1 = await File.ReadAllTextAsync(Path.Combine(dir, "generated", "stage-01-transcript.md"));
        var stage2 = await File.ReadAllTextAsync(Path.Combine(dir, "generated", "stage-02-transcript.md"));
        var stage3 = await File.ReadAllTextAsync(Path.Combine(dir, "generated", "stage-03-transcript.md"));
        var stage4 = await File.ReadAllTextAsync(Path.Combine(dir, "generated", "stage-04-transcript.md"));

        implementationPlan.Should().Contain("Stage 1", because: "implementation-plan.md must summarize the first token ramp stage");
        implementationPlan.Should().Contain("Stage 4", because: "implementation-plan.md must summarize the final heavy stage");
        implementationPlan.Should().Contain("Final Fix Strategy", because: "implementation-plan.md must conclude with a coordinated fix strategy");
        implementationPlan.Should().Contain("20k", because: "implementation-plan.md must reference the heaviest token target");

        reviewReconciliation.Should().Contain("1.", because: "review-reconciliation.md must contain a numbered contradiction list");
        reviewReconciliation.Should().Contain("8.", because: "review-reconciliation.md must contain at least eight contradictions");
        reviewReconciliation.Should().Contain("Coordinator", because: "review-reconciliation.md must reconcile code and review signals");

        ciTriage.Should().Contain("failing suites", because: "ci-triage.md must contain failing suite analysis");
        ciTriage.Should().Contain("probable root causes", because: "ci-triage.md must contain root-cause analysis");
        ciTriage.Should().Contain("confidence", because: "ci-triage.md must capture confidence assessment");

        fixForward.Should().Contain("Coordinator.cs", because: "fix-forward.patch.md must propose changes for Coordinator.cs");
        fixForward.Should().Contain("RetryPolicy.cs", because: "fix-forward.patch.md must propose changes for RetryPolicy.cs");
        fixForward.Should().Contain("StreamingSession.cs", because: "fix-forward.patch.md must propose changes for StreamingSession.cs");

        using var tokenRamp = JsonDocument.Parse(tokenRampReport);
        tokenRamp.RootElement.TryGetProperty("stageTargets", out var stageTargets).Should().BeTrue(
            because: "token-ramp-report.json must contain stageTargets");
        stageTargets.GetArrayLength().Should().Be(4, because: "token-ramp-report.json must describe all four stage targets");

        stage1.Should().Contain("Token Target Hint: 2k", because: "stage 1 transcript must reflect its incremental target");
        stage2.Should().Contain("Token Target Hint: 6k", because: "stage 2 transcript must reflect its incremental target");
        stage3.Should().Contain("Token Target Hint: 7k", because: "stage 3 transcript must reflect its incremental target");
        stage4.Should().Contain("Token Target Hint: 20k", because: "stage 4 transcript must reflect its incremental target");
        stage4.Length.Should().BeGreaterThan(stage1.Length, because: "later transcript stages must be materially heavier than early stages");
    }

    private static async Task WriteRecipeAsync(
        string dir,
        string fileName,
        string stageId,
        string title,
        string primaryObjective,
        string escalationTheme,
        string tokenTargetHint,
        int iterationCount,
        int debatePointCount,
        IReadOnlyList<string> codeTargets,
        IReadOnlyList<string> contextLines)
    {
        var recipe = new
        {
            stageId,
            title,
            primaryObjective,
            escalationTheme,
            tokenTargetHint,
            iterationCount,
            debatePointCount,
            codeTargets,
            contextLines,
        };

        var json = JsonSerializer.Serialize(recipe, new JsonSerializerOptions
        {
            WriteIndented = true,
        });

        await File.WriteAllTextAsync(Path.Combine(dir, "recipes", fileName), json);
    }
}
