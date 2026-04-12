using FluentAssertions;

namespace TokenGuard.Benchmark.AgentWorkflow.Tasks;

/// <summary>
/// Defines a release-audit scenario that makes the model assemble notes, manifest data, and changelog updates.
/// </summary>
internal static class ReleaseAuditTask
{
    private const string CompletionMarker = "RELEASE_AUDIT_COMPLETE";

    /// <summary>
    /// Creates the release-audit task definition consumed by the shared E2E loop.
    /// </summary>
    public static AgentLoopTaskDefinition Create() => new(
        name: "ReleaseAudit",
        conversationName: "e2e-release-audit",
        systemPrompt:
            "You are a release manager assistant running inside a TokenGuard E2E test. " +
            "Your job is to audit workspace files and produce a structured release package. " +
            "You MUST use the provided tools for every file operation — do not invent content without reading source files first. " +
            "Inspect the workspace, read every relevant file, then produce exactly the outputs specified in the task. " +
            "When all outputs are correct, respond with exactly three bullet points. " +
            $"The final bullet must be '{CompletionMarker}'.",
        userMessage:
            "Task: perform a full release audit of the workspace.\n" +
            "Step 1 – list and read all files in the workspace.\n" +
            "Step 2 – create 'release-notes.md' with these four sections in order:\n" +
            "  ## Version (the version string from version.txt)\n" +
            "  ## New Features (all entries from features.txt, one per line)\n" +
            "  ## Bug Fixes (all entries from bugs.txt, one per line)\n" +
            "  ## Breaking Changes (all entries from breaking-changes.txt, one per line)\n" +
            "Step 3 – prepend a new entry to CHANGELOG.md in the format:\n" +
            "  ## [<version>] – Release\n" +
            "  See release-notes.md for details.\n" +
            "  (followed by the existing content of CHANGELOG.md)\n" +
            "Step 4 – create 'release-manifest.json' with fields: version, featureCount, bugFixCount, breakingChangeCount.\n" +
            "         Count entries by reading features.txt, bugs.txt, and breaking-changes.txt.\n" +
            "Step 5 – verify each created file exists and contains the expected content using read operations.\n" +
            "Do not claim completion until all three output files are written and verified.",
        completionMarker: CompletionMarker,
        seedWorkspaceAsync: SeedAsync,
        assertOutcomeAsync: AssertAsync,
        size: TaskSize.Small);

    /// <summary>
    /// Seeds release inputs and historical artefacts for the audit workflow.
    /// </summary>
    private static async Task SeedAsync(string dir)
    {
        await File.WriteAllTextAsync(Path.Combine(dir, "version.txt"),
            "v3.7.1\n");

        await File.WriteAllTextAsync(Path.Combine(dir, "features.txt"),
            string.Join("\n", Enumerable.Range(1, 25).Select(i =>
                $"FEAT-{100 + i}: {FeatureDescriptions[i % FeatureDescriptions.Length]}")));

        await File.WriteAllTextAsync(Path.Combine(dir, "bugs.txt"),
            string.Join("\n", Enumerable.Range(1, 30).Select(i =>
                $"BUG-{200 + i}: {BugDescriptions[i % BugDescriptions.Length]}")));

        await File.WriteAllTextAsync(Path.Combine(dir, "breaking-changes.txt"),
            string.Join("\n", Enumerable.Range(1, 12).Select(i =>
                $"BREAK-{300 + i}: {BreakingDescriptions[i % BreakingDescriptions.Length]}")));

        await File.WriteAllTextAsync(Path.Combine(dir, "CHANGELOG.md"),
            "# Changelog\n\n" +
            string.Join("\n\n", Enumerable.Range(1, 8).Select(i =>
                $"## [v3.{7 - i}.0] – Patch release {i}\n" +
                string.Join("\n", Enumerable.Range(1, 8).Select(j =>
                    $"- Historical fix {i}.{j}: addressed edge case in pipeline stage {j} for version series {i}.")))));

        await File.WriteAllTextAsync(Path.Combine(dir, "release-policy.txt"),
            "Release Policy:\n" +
            "- All feature entries must appear verbatim from features.txt.\n" +
            "- Bug fix entries must appear verbatim from bugs.txt.\n" +
            "- Breaking changes must appear verbatim from breaking-changes.txt.\n" +
            "- CHANGELOG.md must always grow; never replace existing history.\n" +
            "- release-manifest.json must use integer counts, not strings.\n" +
            string.Join("\n", Enumerable.Range(1, 20).Select(i =>
                $"- Policy note {i}: verify all artefacts before marking the release complete.")));
    }

    /// <summary>
    /// Verifies that the release artefacts were generated and existing history was preserved.
    /// </summary>
    private static async Task AssertAsync(string dir, string? finalText)
    {
        var releaseNotes = await File.ReadAllTextAsync(Path.Combine(dir, "release-notes.md"));
        var changelog = await File.ReadAllTextAsync(Path.Combine(dir, "CHANGELOG.md"));
        var manifest = await File.ReadAllTextAsync(Path.Combine(dir, "release-manifest.json"));

        releaseNotes.Should().Contain("v3.7.1", because: "release-notes.md must include the version from version.txt");
        releaseNotes.Should().Contain("## New Features", because: "release-notes.md must contain the New Features section");
        releaseNotes.Should().Contain("## Bug Fixes", because: "release-notes.md must contain the Bug Fixes section");
        releaseNotes.Should().Contain("## Breaking Changes", because: "release-notes.md must contain the Breaking Changes section");
        releaseNotes.Should().Contain("FEAT-", because: "feature entries from features.txt must appear in release-notes.md");
        releaseNotes.Should().Contain("BUG-", because: "bug entries from bugs.txt must appear in release-notes.md");
        releaseNotes.Should().Contain("BREAK-", because: "breaking change entries must appear in release-notes.md");

        changelog.Should().Contain("v3.7.1", because: "CHANGELOG.md must have the new version prepended");
        changelog.Should().Contain("v3.6.0", because: "CHANGELOG.md must still contain the previous version history");

        manifest.Should().Contain("version", because: "manifest must have a version field");
        manifest.Should().Contain("featureCount", because: "manifest must have a featureCount field");
        manifest.Should().Contain("bugFixCount", because: "manifest must have a bugFixCount field");
        manifest.Should().Contain("breakingChangeCount", because: "manifest must have a breakingChangeCount field");
    }

    private static readonly string[] FeatureDescriptions =
    [
        "Sliding window compaction now supports configurable overlap to preserve recent context boundaries.",
        "Conversation context factory exposes a scoped lifetime registration for ASP.NET Core middleware pipelines.",
        "Token usage telemetry can now be forwarded to OpenTelemetry exporters via the new metrics bridge.",
        "Added batch-message ingestion API allowing callers to enqueue multiple user turns atomically.",
        "Profile-based compaction strategies allow teams to switch between aggressive and conservative modes.",
        "SDK now emits structured log events for every compaction cycle with masked-message counts.",
        "New extension method ForAzureOpenAI adapts prepared messages for the Azure OpenAI service SDK.",
        "System-prompt pinning ensures the instruction segment is never masked during sliding-window compaction.",
        "Thread-safe conversation context supports concurrent read operations during async PrepareAsync calls.",
        "IConversationContextFactory now supports named scopes enabling per-request context isolation.",
    ];

    private static readonly string[] BugDescriptions =
    [
        "Fixed a race condition in PrepareAsync when two threads simultaneously triggered threshold evaluation.",
        "Corrected off-by-one error in sliding-window boundary calculation for odd-numbered message sequences.",
        "Resolved NullReferenceException thrown when RecordToolResult was called before any model response.",
        "Fixed token-count accumulation resetting incorrectly after a partial compaction cycle.",
        "Corrected message ordering regression introduced in the builder refactor that swapped user and assistant turns.",
        "Fixed memory leak in ConversationContext caused by retained SegmentList references after Dispose.",
        "Resolved serialization mismatch in OpenAI extension where tool-call IDs exceeded 64 characters.",
        "Fixed CompactionState enum not being persisted across PrepareAsync calls in streaming scenarios.",
        "Corrected threshold percentage interpretation: 0.80 now correctly means 80 percent, not 8 percent.",
        "Fixed regression where SetSystemPrompt called twice would append rather than replace the instruction.",
    ];

    private static readonly string[] BreakingDescriptions =
    [
        "IConversationContext.PrepareAsync now returns IReadOnlyList<PreparedMessage> instead of IEnumerable.",
        "ConversationConfigBuilder.WithMaxTokens now requires a positive non-zero integer; zero throws ArgumentOutOfRangeException.",
        "RecordModelResponse signature changed: inputTokens parameter moved to position 2 for consistency.",
        "DefaultCompactionProfile renamed to BalancedCompactionProfile; update all service-registration call sites.",
        "TokenGuard.Extensions.OpenAI assembly now targets net8.0 minimum; net6.0 support removed.",
        "ForOpenAI extension method now throws InvalidOperationException if any message has an unknown state.",
    ];
}
