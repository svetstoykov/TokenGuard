using System.Text.Json;

namespace Codexplorer.Automation.AgentComparison;

/// <summary>Validates and compares captured per-task reports without an inference client.</summary>
internal static class ComparisonReportCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Asynchronously displays outcomes, separate checks, nullable usage and optional matched savings.</summary>
    /// <param name="reportsRoot">The invocation directory containing session reports.</param>
    /// <param name="baseline">The explicitly named baseline arm, or null to omit savings.</param>
    /// <param name="writer">The output destination.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task containing zero for valid inspectable reports.</returns>
    internal static async Task<int> RunAsync(string reportsRoot, string? baseline, TextWriter writer, CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(reportsRoot);
        var reports = new List<(string Path, ComparisonExecutionReport Report)>();
        foreach (var path in FindReports(root))
        {
            await using var stream = File.OpenRead(path);
            var report = await JsonSerializer.DeserializeAsync<ComparisonExecutionReport>(stream, JsonOptions, cancellationToken)
                ?? throw new InvalidDataException("A comparison report is empty.");
            ValidateReport(path, report);
            if (!report.Synthetic)
                await ValidateProvenanceAsync(path, report, cancellationToken);
            reports.Add((path, report));
        }
        if (reports.Count == 0)
            throw new InvalidDataException("No comparison schema 1 session reports were found.");
        await writer.WriteLineAsync("| Tier/task | Arm/repetition | Outcome | Evidence | Final answer | Input | Output | Cost | Timeline |");
        await writer.WriteLineAsync("| --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var (path, report) in reports.OrderBy(item => item.Report.Session.Session.Tier, StringComparer.Ordinal)
            .ThenBy(item => item.Report.Session.Session.Task.TaskId, StringComparer.Ordinal))
        {
            var session = report.Session;
            var plan = session.Session;
            var usage = report.Usage;
            var timeline = Path.GetRelativePath(root, Path.Combine(Path.GetDirectoryName(path)!, session.RequestTimelinePath));
            await writer.WriteLineAsync($"| {plan.Tier}/{plan.Task.TaskId} | {plan.Arm}/{plan.Repetition} | "
                + $"{session.Runtime.Outcome}{(session.Runtime.Partial ? " (partial)" : "")}{(report.Synthetic ? " SYNTHETIC" : "")} | "
                + $"{session.EvidenceChecks.Count(check => check.Passed)}/{session.EvidenceChecks.Count} | "
                + $"{session.FinalAnswerChecks.Count(check => check.Passed)}/{session.FinalAnswerChecks.Count} | "
                + $"{usage.InputTokens?.ToString() ?? "unknown"} | {usage.OutputTokens?.ToString() ?? "unknown"} | "
                + $"{usage.Cost?.ToString() ?? "unknown"} | [timeline]({timeline}) |");
            if (!usage.CompleteTokens || !usage.CompleteCost)
                await writer.WriteLineAsync($"INCOMPLETE ACCOUNTING: {plan.Id}; unknown usage attempts={usage.UnknownUsageAttempts}; "
                    + $"known partial cost={usage.KnownCost?.ToString() ?? "unknown"}; failures={usage.FailedAttempts}.");
        }
        await writer.WriteLineAsync("Failed attempts remain in spend totals; cached/reasoning values are subsets. Checks score notes and answers.");
        if (reports.Any(item => item.Report.Synthetic))
            await writer.WriteLineAsync("Synthetic setup evidence provides no performance conclusion or savings estimate.");
        else if (baseline is not null)
            await WriteSavingsAsync(reports.Select(item => item.Report).ToArray(), baseline, writer);
        return 0;
    }

    private static void ValidateReport(string path, ComparisonExecutionReport report)
    {
        var session = report.Session;
        if (report.SchemaVersion != 1 || report.Synthetic && report.LiveExecutionStarted
            || session.Session.Task.Probe is not null || session.Session.Task.TaskId is null
            || session.Runtime.Outcome is not ("completed" or "failed" or "cancelled" or "budgetExhausted")
            || report.Attempts.Select(attempt => attempt.Sequence).Distinct().Count() != report.Attempts.Count
            || report.Attempts.Any(attempt => attempt.SessionId != session.Session.Id))
            throw new InvalidDataException("A report violates the independent comparison schema or session association.");
        var computed = ComparisonAccounting.Aggregate(report.Attempts);
        if (computed != report.Usage)
            throw new InvalidDataException("A report's totals do not match its independently captured attempts.");
        var expectedEvidence = (session.Session.Task.Checks ?? []).Where(check => check.Artifact is not null).Select(check => check.Id);
        var expectedAnswer = (session.Session.Task.Checks ?? []).Where(check => check.Artifact is null).Select(check => check.Id);
        if (!expectedEvidence.SequenceEqual(session.EvidenceChecks.Select(check => check.Id))
            || !expectedAnswer.SequenceEqual(session.FinalAnswerChecks.Select(check => check.Id)))
            throw new InvalidDataException("A report changed or pooled the original check populations.");
        var sessionRoot = Path.GetDirectoryName(path)!;
        ComparisonArtifacts.Resolve(sessionRoot, session.RequestTimelinePath);
        ComparisonArtifacts.Resolve(sessionRoot, session.DiffPath);
        foreach (var artifact in session.Artifacts)
        {
            var file = ComparisonArtifacts.Resolve(Path.Combine(sessionRoot, "artifacts"), artifact.Path);
            if (!File.Exists(file) || ComparisonArtifacts.Hash(file) != artifact.Sha256)
                throw new InvalidDataException("A report artifact is missing or differs from its inventory.");
        }
        foreach (var attempt in report.Attempts)
        {
            if (attempt.CaptureFiles is not { } files)
                throw new InvalidDataException("A report attempt has no exact capture references.");
            foreach (var relative in new[] { files.ClientRequest, files.EffectiveRequest, files.Response, files.Normalization }.OfType<string>())
                if (!File.Exists(ComparisonArtifacts.Resolve(Path.Combine(sessionRoot, "recording"), relative)))
                    throw new InvalidDataException("A report's exact request/response capture is missing.");
        }
    }

    private static async Task ValidateProvenanceAsync(string path, ComparisonExecutionReport report, CancellationToken cancellationToken)
    {
        ComparisonLaunchPlan? plan = null;
        for (var current = new DirectoryInfo(Path.GetDirectoryName(path)!); current is not null; current = current.Parent)
        {
            var candidate = Path.Combine(current.FullName, "scratch", "launch-plan.json");
            if (!File.Exists(candidate))
                continue;
            plan = await ComparisonPlanFile.LoadAsync(candidate, cancellationToken);
            break;
        }
        if (plan is null || plan.PlanDigest != report.PlanDigest)
            throw new InvalidDataException("A captured report does not bind the available immutable launch plan.");
        var expected = plan.Sessions.SingleOrDefault(session => session.Id == report.Session.Session.Id);
        if (expected is null || JsonSerializer.Serialize(expected, JsonOptions) != JsonSerializer.Serialize(report.Session.Session, JsonOptions))
            throw new InvalidDataException("A report's task/prompt/settings provenance differs from the bound plan.");
    }

    private static async Task WriteSavingsAsync(IReadOnlyList<ComparisonExecutionReport> reports, string baseline, TextWriter writer)
    {
        foreach (var tier in reports.GroupBy(report => report.Session.Session.Tier, StringComparer.Ordinal))
        {
            var control = tier.Where(report => report.Session.Session.Arm == baseline).ToArray();
            foreach (var arm in tier.Select(report => report.Session.Session.Arm).Distinct(StringComparer.Ordinal).Where(arm => arm != baseline))
            {
                var candidate = tier.Where(report => report.Session.Session.Arm == arm).ToArray();
                var population = control.Select(PopulationKey).Order(StringComparer.Ordinal).ToArray();
                var matches = population.Length > 0 && population.Distinct(StringComparer.Ordinal).Count() == population.Length
                    && population.SequenceEqual(candidate.Select(PopulationKey).Order(StringComparer.Ordinal), StringComparer.Ordinal);
                var all = control.Concat(candidate).ToArray();
                if (!matches || all.Any(report => report.Session.Runtime.Partial || !report.Usage.CompleteTokens || !report.Usage.CompleteCost)
                    || all.Select(report => report.PlanDigest).Distinct(StringComparer.Ordinal).Count() != 1)
                {
                    await writer.WriteLineAsync(
                        $"Savings skipped for {tier.Key}/{arm} against named baseline {baseline}: incomplete matching population.");
                    continue;
                }
                var baselineTokens = control.Sum(report => report.Usage.InputTokens!.Value + report.Usage.OutputTokens!.Value);
                var candidateTokens = candidate.Sum(report => report.Usage.InputTokens!.Value + report.Usage.OutputTokens!.Value);
                var baselineCost = control.Sum(report => report.Usage.Cost!.Value);
                var candidateCost = candidate.Sum(report => report.Usage.Cost!.Value);
                var tokenSaving = baselineTokens == 0 ? (decimal?)null : 100m * (baselineTokens - candidateTokens) / baselineTokens;
                var costSaving = baselineCost == 0 ? (decimal?)null : 100m * (baselineCost - candidateCost) / baselineCost;
                await writer.WriteLineAsync($"{tier.Key}/{arm} against named baseline {baseline}: "
                    + $"token saving={tokenSaving?.ToString("F2") ?? "unknown"}%; cost saving={costSaving?.ToString("F2") ?? "unknown"}%.");
            }
        }
    }

    private static string PopulationKey(ComparisonExecutionReport report) =>
        $"{report.Session.Session.Task.TaskId}/{report.Session.Session.Repetition}/{report.Session.Session.SourcePromptSha256}";

    private static IEnumerable<string> FindReports(string root)
    {
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException("The comparison report directory does not exist.");
        ComparisonArtifacts.Resolve(root, ".");
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            var candidate = Path.Combine(directory, "report.json");
            if (File.Exists(candidate))
                yield return ComparisonArtifacts.Resolve(root, Path.GetRelativePath(root, candidate));
            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                if (Path.GetFileName(child) is "checkout" or "state" or "tools" or ".git"
                    || new DirectoryInfo(child).LinkTarget is not null)
                    continue;
                pending.Push(child);
            }
        }
    }
}
