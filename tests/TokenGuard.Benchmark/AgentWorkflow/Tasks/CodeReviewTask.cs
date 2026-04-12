using FluentAssertions;
using TokenGuard.Benchmark.AgentWorkflow.Tasks;

namespace TokenGuard.E2E.Tasks;

/// <summary>
/// Defines a seeded code-review scenario that forces the model to inspect multiple flawed source files.
/// </summary>
internal static class CodeReviewTask
{
    private const string CompletionMarker = "CODE_REVIEW_COMPLETE";

    /// <summary>
    /// Creates the code-review task definition consumed by the shared E2E loop.
    /// </summary>
    public static AgentLoopTaskDefinition Create() => new(
        name: "CodeReview",
        conversationName: "e2e-code-review",
        systemPrompt:
            "You are a code-review assistant running inside a TokenGuard E2E test. " +
            "Your job is to read source files, apply the review rules from review-config.txt, and produce review artefacts. " +
            "You MUST use the provided tools for every read and write — do not invent issues without reading the actual source files. " +
            "When all artefacts are complete, respond with exactly three bullet points. " +
            $"The final bullet must be '{CompletionMarker}'.",
        userMessage:
            "Task: perform a structured code review of the workspace.\n" +
            "Step 1 – list all files, then read review-config.txt to understand severity rules.\n" +
            "Step 2 – read each source file (module-core.cs, module-auth.cs, module-data.cs) and note any issues.\n" +
            "Step 3 – create 'review-report.md' with a section per module. Each section must list:\n" +
            "  - The module name as a heading.\n" +
            "  - Each issue found, prefixed with its severity label (CRITICAL, HIGH, MEDIUM, LOW).\n" +
            "Step 4 – create 'action-items.txt' with a numbered list of remediation steps, one per issue, ordered by severity (CRITICAL first).\n" +
            "Step 5 – edit each of the three source files to prepend the single line: // Reviewed: pending remediation\n" +
            "Step 6 – read back each artefact to confirm it contains the expected content.\n" +
            "Do not claim completion until review-report.md, action-items.txt, and all three source-file headers are in place.",
        completionMarker: CompletionMarker,
        seedWorkspaceAsync: SeedAsync,
        assertOutcomeAsync: AssertAsync);

    /// <summary>
    /// Seeds a workspace with review rules, flawed modules, and enough context volume to trigger tool use.
    /// </summary>
    private static async Task SeedAsync(string dir)
    {
        await File.WriteAllTextAsync(Path.Combine(dir, "review-config.txt"),
            "Code Review Severity Rules\n" +
            "==========================\n" +
            "CRITICAL – security vulnerability, data loss risk, or crash on any input.\n" +
            "HIGH     – incorrect behaviour under documented contract; causes test failures.\n" +
            "MEDIUM   – deviation from coding standards; degrades maintainability.\n" +
            "LOW      – minor style or naming inconsistency; no functional impact.\n\n" +
            "Automatic CRITICAL triggers:\n" +
            "  - Raw SQL string concatenation (SQL injection risk).\n" +
            "  - Password or secret stored as plain string field.\n" +
            "  - Unhandled exception swallowed in a catch block with empty body.\n\n" +
            "Automatic HIGH triggers:\n" +
            "  - Nullable reference not guarded before dereference.\n" +
            "  - Async method missing await (fire-and-forget without intent).\n" +
            "  - Collection modified inside foreach loop.\n\n" +
            "Automatic MEDIUM triggers:\n" +
            "  - Public method missing XML summary comment.\n" +
            "  - Magic number literal not assigned to a named constant.\n" +
            "  - Method exceeding 40 lines without decomposition.\n\n" +
            "Automatic LOW triggers:\n" +
            "  - Variable name shorter than three characters (excluding loop counters).\n" +
            "  - Missing trailing newline at end of file.\n" +
            string.Join("\n", Enumerable.Range(1, 15).Select(i =>
                $"  - Style note {i}: see internal wiki for detailed coding standard reference.")));

        await File.WriteAllTextAsync(Path.Combine(dir, "module-core.cs"),
            "// module-core.cs – Core pipeline orchestration\n" +
            "using System;\nusing System.Collections.Generic;\n\n" +
            "public class PipelineOrchestrator\n{\n" +
            "    private string secret = \"hardcoded-secret-key-abc123\"; // ISSUE: plain-text secret\n\n" +
            "    public void RunPipeline(List<string> stages)\n    {\n" +
            "        for (int i = 0; i < stages.Count; i++)\n        {\n" +
            "            stages.Add(\"extra-stage\"); // ISSUE: modifying list inside loop\n" +
            "            Console.WriteLine(stages[i]);\n        }\n    }\n\n" +
            "    public string BuildQuery(string userInput)\n    {\n" +
            "        return \"SELECT * FROM records WHERE name = '\" + userInput + \"'\"; // ISSUE: SQL injection\n" +
            "    }\n\n" +
            string.Join("\n", Enumerable.Range(1, 20).Select(i =>
                $"    // Core note {i}: pipeline stage {i} configuration applied at construction time.")) +
            "\n}\n");

        await File.WriteAllTextAsync(Path.Combine(dir, "module-auth.cs"),
            "// module-auth.cs – Authentication and session management\n" +
            "using System;\nusing System.Threading.Tasks;\n\n" +
            "public class AuthService\n{\n" +
            "    public async Task<bool> ValidateTokenAsync(string? token)\n    {\n" +
            "        var len = token.Length; // ISSUE: nullable dereference without null check\n" +
            "        return len > 0;\n    }\n\n" +
            "    public async Task RefreshSessionAsync(string sessionId)\n    {\n" +
            "        _ = DoRefreshAsync(sessionId); // ISSUE: async fire-and-forget without await\n" +
            "    }\n\n" +
            "    private async Task DoRefreshAsync(string id)\n    {\n" +
            "        try { await Task.Delay(100); }\n" +
            "        catch { } // ISSUE: swallowed exception\n" +
            "    }\n\n" +
            "    public int Timeout = 30; // ISSUE: magic number, no named constant\n\n" +
            string.Join("\n", Enumerable.Range(1, 22).Select(i =>
                $"    // Auth note {i}: session rotation policy applied every {i * 5} minutes.")) +
            "\n}\n");

        await File.WriteAllTextAsync(Path.Combine(dir, "module-data.cs"),
            "// module-data.cs – Data access and persistence layer\n" +
            "using System;\nusing System.Collections.Generic;\nusing System.Linq;\n\n" +
            "public class DataRepository\n{\n" +
            "    public List<string> GetAll()\n    {\n" +
            "        var rs = FetchFromStore();\n" + // ISSUE: short variable name
            "        return rs.ToList();\n    }\n\n" +
            "    private IEnumerable<string> FetchFromStore()\n    {\n" +
            "        return Enumerable.Range(1, 500).Select(i => $\"record-{i}\");\n" +
            "    }\n\n" +
            "    public void ProcessBatch(IEnumerable<string> items)\n    {\n" +
            string.Join("\n", Enumerable.Range(1, 42).Select(i =>
                $"        // Processing step {i}: validate, transform, and persist item segment {i}.")) +
            "\n    }\n\n" + // ISSUE: method exceeds 40 lines
            string.Join("\n", Enumerable.Range(1, 18).Select(i =>
                $"    // Data note {i}: repository pattern enforces unit-of-work boundary at transaction {i}.")) +
            "\n}\n");
    }

    /// <summary>
    /// Verifies that the review artefacts and source-file review markers were produced.
    /// </summary>
    private static async Task AssertAsync(string dir, string? finalText)
    {
        var report = await File.ReadAllTextAsync(Path.Combine(dir, "review-report.md"));
        var actionItems = await File.ReadAllTextAsync(Path.Combine(dir, "action-items.txt"));
        var core = await File.ReadAllTextAsync(Path.Combine(dir, "module-core.cs"));
        var auth = await File.ReadAllTextAsync(Path.Combine(dir, "module-auth.cs"));
        var data = await File.ReadAllTextAsync(Path.Combine(dir, "module-data.cs"));

        report.Should().Contain("module-core", because: "review-report.md must have a section for module-core");
        report.Should().Contain("module-auth", because: "review-report.md must have a section for module-auth");
        report.Should().Contain("module-data", because: "review-report.md must have a section for module-data");
        report.Should().Contain("CRITICAL", because: "review-report.md must flag the critical issues found in source files");
        report.Should().Contain("HIGH", because: "review-report.md must flag the high-severity issues");

        actionItems.Should().Contain("1.", because: "action-items.txt must be a numbered list");
        actionItems.Should().NotBeNullOrWhiteSpace(because: "action-items.txt must contain remediation steps");

        core.Should().StartWith("// Reviewed:", because: "module-core.cs must be edited to include the review header");
        auth.Should().StartWith("// Reviewed:", because: "module-auth.cs must be edited to include the review header");
        data.Should().StartWith("// Reviewed:", because: "module-data.cs must be edited to include the review header");
    }
}
