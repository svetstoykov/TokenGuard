using FluentAssertions;

namespace TokenGuard.Benchmark.AgentWorkflow.Tasks;

/// <summary>
/// Defines an incident-registry scenario that forces the model to process eight small incident snippets
/// sequentially, appending each to a growing registry — naturally producing many turns with an
/// ever-larger tool result as the registry grows, which makes observation masking progressively more valuable.
/// </summary>
internal static class IncidentRegistryTask
{
    private const string CompletionMarker = "INCIDENT_REGISTRY_COMPLETE";

    /// <summary>
    /// Creates the incident-registry task definition consumed by the shared E2E loop.
    /// </summary>
    public static AgentLoopTaskDefinition Create() => new(
        name: "IncidentRegistry",
        conversationName: "e2e-incident-registry",
        systemPrompt:
            "You are an SRE incident registry assistant running inside a TokenGuard E2E test. " +
            "Your job is to read incident report snippets and build a structured registry file. " +
            "You MUST use the provided tools for every file operation — never invent or skip content. " +
            "Process incidents strictly in order: incident-01.txt through incident-08.txt. " +
            "Finish all steps for one incident before moving to the next. " +
            "Because edit_text_file replaces the full file content, you must read the registry first, " +
            "then write the entire updated content including the new entry. " +
            "When the registry and summary are complete, respond with exactly three bullet points. " +
            $"The final bullet must be '{CompletionMarker}'.",
        userMessage:
            "Task: build an incident registry from eight incident report files.\n" +
            "Step 1 – list all workspace files.\n" +
            "Step 2 – read registry-template.txt to understand the entry format.\n" +
            "Step 3 – create incident-registry.md with only this header line: '# Incident Registry'\n" +
            "Step 4 – for each incident file in order (incident-01.txt through incident-08.txt), do exactly:\n" +
            "  a. Read the incident file.\n" +
            "  b. Read the current incident-registry.md.\n" +
            "  c. Edit incident-registry.md to append the new entry (keep all existing entries, add the new one at the end).\n" +
            "Step 5 – read the final incident-registry.md and confirm all 8 incident IDs are present.\n" +
            "Step 6 – create summary.txt with a count of incidents by severity:\n" +
            "  P1: <count>\n" +
            "  P2: <count>\n" +
            "  P3: <count>\n" +
            "Do not claim completion until incident-registry.md contains all 8 incident IDs and summary.txt exists.",
        completionMarker: CompletionMarker,
        seedWorkspaceAsync: SeedAsync,
        assertOutcomeAsync: AssertAsync,
        size: TaskSize.Small);

    private static async Task SeedAsync(string dir)
    {
        await File.WriteAllTextAsync(Path.Combine(dir, "registry-template.txt"),
            "Incident Registry Entry Format\n" +
            "==============================\n" +
            "## <INCIDENT-ID>: <title>\n" +
            "- Severity: <P1|P2|P3>\n" +
            "- Service: <service-name>\n" +
            "- Duration: <minutes> min\n" +
            "- Root cause: <one sentence>\n" +
            "- Mitigation: <one sentence>\n\n" +
            "Severity definitions:\n" +
            "  P1 = customer-facing outage or revenue impact\n" +
            "  P2 = degraded service, partial customer impact\n" +
            "  P3 = internal system issue, no customer impact\n");

        var incidents = new (string File, string Id, string Title, string Severity, string Service, int Duration, string RootCause, string Mitigation)[]
        {
            ("incident-01.txt", "INC-001", "Payment gateway timeout",      "P1", "payment",       45,  "Database connection pool exhausted during flash sale",             "Increased pool size and added circuit breaker"),
            ("incident-02.txt", "INC-002", "Auth service memory leak",     "P2", "auth",          120, "Unbounded cache growth in session validator",                      "Deployed hotfix with TTL enforcement"),
            ("incident-03.txt", "INC-003", "Inventory sync lag",           "P3", "inventory",     30,  "Third-party warehouse API rate limit hit",                         "Implemented exponential backoff retry"),
            ("incident-04.txt", "INC-004", "Notification delivery failure","P2", "notifications", 60,  "SQS queue consumer crashed on malformed message",                 "Added dead-letter queue and message validation"),
            ("incident-05.txt", "INC-005", "Report generation timeout",    "P3", "reporting",     20,  "Unindexed query executed against large historical dataset",        "Added composite index on date and user_id columns"),
            ("incident-06.txt", "INC-006", "API gateway 502 errors",       "P1", "gateway",       15,  "Upstream health check misconfigured after deploy",                 "Rolled back deploy and corrected health check endpoint"),
            ("incident-07.txt", "INC-007", "Batch import data corruption", "P2", "inventory",     90,  "Character encoding mismatch between CSV exporter and importer",   "Standardised UTF-8 encoding across pipeline"),
            ("incident-08.txt", "INC-008", "Dashboard load failure",       "P3", "reporting",     25,  "Expired CDN cache invalidation token",                             "Rotated CDN credentials and automated renewal"),
        };

        foreach (var (file, id, title, severity, service, duration, rootCause, mitigation) in incidents)
        {
            var content =
                $"Incident ID: {id}\n" +
                $"Title: {title}\n" +
                $"Severity: {severity}\n" +
                $"Service: {service}\n" +
                $"Duration: {duration} min\n" +
                $"Root cause: {rootCause}\n" +
                $"Mitigation: {mitigation}\n";

            await File.WriteAllTextAsync(Path.Combine(dir, file), content);
        }
    }

    private static async Task AssertAsync(string dir, string? finalText)
    {
        var registry = await File.ReadAllTextAsync(Path.Combine(dir, "incident-registry.md"));

        var expectedIds = new[] { "INC-001", "INC-002", "INC-003", "INC-004", "INC-005", "INC-006", "INC-007", "INC-008" };
        foreach (var id in expectedIds)
        {
            registry.Should().Contain(id,
                because: $"incident-registry.md must contain an entry for {id}");
        }

        registry.Should().Contain("P1",
            because: "incident-registry.md must include P1 severity incidents");
        registry.Should().Contain("P2",
            because: "incident-registry.md must include P2 severity incidents");
        registry.Should().Contain("P3",
            because: "incident-registry.md must include P3 severity incidents");

        var summaryPath = Path.Combine(dir, "summary.txt");
        File.Exists(summaryPath).Should().BeTrue(
            because: "summary.txt must be created with severity counts");

        var summary = await File.ReadAllTextAsync(summaryPath);
        summary.Should().NotBeNullOrWhiteSpace(
            because: "summary.txt must contain incident severity counts");
    }
}
