using FluentAssertions;

namespace TokenGuard.E2E.Tasks;

/// <summary>
/// Defines a dependency-audit scenario with mixed vulnerability and licence outcomes.
/// </summary>
internal static class DependencyAuditTask
{
    private const string CompletionMarker = "DEPENDENCY_AUDIT_COMPLETE";

    /// <summary>
    /// Creates the dependency-audit task definition consumed by the shared E2E loop.
    /// </summary>
    public static AgentLoopTaskDefinition Create() => new(
        name: "DependencyAudit",
        conversationName: "e2e-dependency-audit",
        systemPrompt:
            "You are a dependency security auditor running inside a TokenGuard E2E test. " +
            "Your job is to read the workspace dependency and advisory files, cross-reference them, and produce audit artefacts. " +
            "You MUST use the provided tools for every file operation. " +
            "Read every file before writing any output. Be systematic: check every dependency against every advisory entry. " +
            "When all artefacts are complete, respond with exactly three bullet points. " +
            $"The final bullet must be '{CompletionMarker}'.",
        userMessage:
            "Task: perform a full dependency security and licence audit.\n" +
            "Step 1 – list all workspace files, then read them all: current-deps.txt, security-advisories.txt, license-policy.txt, and upgrade-template.txt.\n" +
            "Step 2 – create 'audit-report.md' with these sections:\n" +
            "  ## Vulnerable Dependencies – list every dep from current-deps.txt whose version matches an advisory in security-advisories.txt.\n" +
            "  ## Licence Violations – list every dep whose declared licence is prohibited by license-policy.txt.\n" +
            "  ## Clean Dependencies – list deps that passed both checks.\n" +
            "Step 3 – create 'upgrade-plan.txt' with one line per vulnerable dependency in the format:\n" +
            "  <package-name>: upgrade from <current-version> to <safe-version> (CVE: <cve-id>)\n" +
            "  Use the safe-version and CVE from security-advisories.txt.\n" +
            "Step 4 – create 'compliance-matrix.txt' listing every dependency with its status: PASS or FAIL and the reason.\n" +
            "Step 5 – read back each created file and confirm it is correct.\n" +
            "Do not claim completion until audit-report.md, upgrade-plan.txt, and compliance-matrix.txt all exist and are populated.",
        completionMarker: CompletionMarker,
        seedWorkspaceAsync: SeedAsync,
        assertOutcomeAsync: AssertAsync);

    /// <summary>
    /// Seeds dependency, advisory, and policy files that require cross-file reasoning and output generation.
    /// </summary>
    private static async Task SeedAsync(string dir)
    {
        await File.WriteAllTextAsync(Path.Combine(dir, "current-deps.txt"),
            "# Current project dependencies\n" +
            "# Format: package-name version licence\n\n" +
            string.Join("\n",
            [
                "Newtonsoft.Json 12.0.1 MIT",
                "Serilog 2.10.0 Apache-2.0",
                "Dapper 1.50.0 Apache-2.0",
                "AutoMapper 10.0.0 MIT",
                "FluentValidation 9.0.0 Apache-2.0",
                "MediatR 9.0.0 Apache-2.0",
                "StackExchange.Redis 2.1.0 MIT",
                "Polly 7.1.0 BSD-3-Clause",
                "NLog 4.7.0 BSD-3-Clause",
                "log4net 2.0.8 Apache-2.0",
                "ImageSharp 1.0.0 Six-Labors-Commercial",
                "PdfiumViewer 2.13.0 Apache-2.0",
                "SharpZipLib 1.3.0 MIT",
                "HtmlAgilityPack 1.11.28 MIT",
                "RestSharp 106.6.7 Apache-2.0",
                "Refit 5.0.0 MIT",
                "Bogus 33.0.0 MIT",
                "CsvHelper 26.0.0 MS-PL",
                "LiteDB 5.0.9 MIT",
                "Hangfire 1.7.14 LGPL-3.0",
            ]) +
            "\n");

        await File.WriteAllTextAsync(Path.Combine(dir, "security-advisories.txt"),
            "# Security advisories – vulnerabilities in specific package versions\n" +
            "# Format: package-name affected-version-range safe-version CVE-ID severity\n\n" +
            string.Join("\n",
            [
                "Newtonsoft.Json <=12.0.3 13.0.1 CVE-2024-21907 HIGH",
                "Dapper <=1.60.0 2.0.123 CVE-2024-10001 CRITICAL",
                "log4net <=2.0.10 2.0.15 CVE-2024-12345 HIGH",
                "StackExchange.Redis <=2.2.0 2.6.111 CVE-2023-99876 MEDIUM",
                "HtmlAgilityPack <=1.11.40 1.11.61 CVE-2024-56789 HIGH",
                "RestSharp <=107.0.0 110.2.0 CVE-2024-33333 MEDIUM",
                "SharpZipLib <=1.3.3 1.4.2 CVE-2024-77777 LOW",
            ]) +
            "\n\n" +
            string.Join("\n", Enumerable.Range(1, 20).Select(i =>
                $"# Advisory note {i}: always verify the safe-version from the NVD database before upgrading.")));

        await File.WriteAllTextAsync(Path.Combine(dir, "license-policy.txt"),
            "# Licence Policy – prohibited and restricted licences\n\n" +
            "PROHIBITED (must not be used in production):\n" +
            "  - AGPL-3.0\n" +
            "  - LGPL-3.0\n" +
            "  - Six-Labors-Commercial\n" +
            "  - MS-PL\n\n" +
            "RESTRICTED (requires legal review before use):\n" +
            "  - GPL-2.0\n" +
            "  - GPL-3.0\n" +
            "  - EUPL-1.2\n\n" +
            "APPROVED (no further review needed):\n" +
            "  - MIT\n" +
            "  - Apache-2.0\n" +
            "  - BSD-3-Clause\n" +
            "  - BSD-2-Clause\n" +
            "  - ISC\n\n" +
            string.Join("\n", Enumerable.Range(1, 18).Select(i =>
                $"# Policy note {i}: licence compliance review is mandatory before every major release.")));

        await File.WriteAllTextAsync(Path.Combine(dir, "upgrade-template.txt"),
            "# Upgrade plan template\n" +
            "# Use this format for each vulnerable dependency:\n" +
            "#   <package-name>: upgrade from <current-version> to <safe-version> (CVE: <cve-id>)\n\n" +
            "Guidelines:\n" +
            "  1. Group upgrades by severity: CRITICAL first, then HIGH, MEDIUM, LOW.\n" +
            "  2. Test each upgrade in isolation before merging.\n" +
            "  3. Check for API-breaking changes between current and safe version.\n" +
            "  4. Update integration tests after each upgrade.\n" +
            string.Join("\n", Enumerable.Range(1, 15).Select(i =>
                $"  {i + 4}. Upgrade guideline {i}: refer to the internal upgrade runbook for package category {i}.")));
    }

    /// <summary>
    /// Verifies that the generated audit artefacts reflect both vulnerable and compliant dependencies.
    /// </summary>
    private static async Task AssertAsync(string dir, string? finalText)
    {
        var report = await File.ReadAllTextAsync(Path.Combine(dir, "audit-report.md"));
        var upgradePlan = await File.ReadAllTextAsync(Path.Combine(dir, "upgrade-plan.txt"));
        var matrix = await File.ReadAllTextAsync(Path.Combine(dir, "compliance-matrix.txt"));

        report.Should().Contain("Vulnerable Dependencies", because: "audit-report.md must have a vulnerable dependencies section");
        report.Should().Contain("Licence Violations", because: "audit-report.md must have a licence violations section");
        report.Should().Contain("Clean Dependencies", because: "audit-report.md must have a clean dependencies section");
        report.Should().Contain("Newtonsoft.Json", because: "Newtonsoft.Json is vulnerable and must appear in the report");
        report.Should().Contain("Dapper", because: "Dapper has a critical advisory and must appear in the report");
        report.Should().Contain("Hangfire", because: "Hangfire has a prohibited licence and must appear in the report");
        report.Should().Contain("ImageSharp", because: "ImageSharp has a prohibited licence and must appear in the report");

        upgradePlan.Should().Contain("Dapper", because: "the upgrade plan must include Dapper (CRITICAL vulnerability)");
        upgradePlan.Should().Contain("CVE-", because: "each upgrade line must reference a CVE identifier");
        upgradePlan.Should().Contain("upgrade from", because: "upgrade plan lines must follow the prescribed format");

        matrix.Should().Contain("PASS", because: "compliance matrix must mark passing dependencies");
        matrix.Should().Contain("FAIL", because: "compliance matrix must mark failing dependencies");
        matrix.Should().Contain("Newtonsoft.Json", because: "compliance matrix must include every dependency");
    }
}
