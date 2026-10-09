using System.Text.Json;
using Codexplorer.Automation.Scoring;

namespace Codexplorer.Automation.Reporting;

/// <summary>
///     Provides report consistency and control-arm validation.
/// </summary>
/// <remarks>Validation recomputes metrics from the recorded populations and compares distributions structurally.</remarks>
internal static class ReportValidator
{
    /// <summary>
    ///     Validates report measurements, totals, provenance, and control coverage.
    /// </summary>
    /// <param name="report">The allowlisted report to finalize or validate. Cannot be <see langword="null" />.</param>
    /// <returns>The validation errors, or an empty list when the report is valid.</returns>
    public static IReadOnlyList<string> Validate(RunReport report)
    {
        var errors = new List<string>();
        if (report.SchemaVersion != 3)
        {
            errors.Add("Unsupported report schema version; expected 3.");
            return errors;
        }

        if (report.Run is null || report.Tasks is null || report.Totals is null || report.UnrunTaskIds is null || report.Validation is null)
        {
            errors.Add("Report contains a null required section.");
            return errors;
        }

        errors.AddRange(GetCollectionErrors(report.Run, report.Tasks, report.UnrunTaskIds));
        if (new[] { report.Totals.ProtocolCompletionRate, report.Totals.DeliverableCompletionRate, report.Totals.ProbePassRate }
                .Any(value => value.HasValue && !double.IsFinite(value.Value))
            || report.Totals.Metrics is null || HasNonfiniteMetric(report.Totals.Metrics))
        {
            errors.Add("Report totals contain missing or nonfinite metrics.");
            return errors;
        }

        var aggregator = new ReportAggregator();
        foreach (var task in report.Tasks)
        {
            if (task is null || task.Metrics is null || task.PrepareRecords is null || task.ProviderCalls is null
                || task.HelperResponses is null || task.SummarizerResponses is null || task.ArtifactsAtStart is null
                || task.ArtifactsAtEnd is null || task.Checks is null)
            {
                errors.Add("Task contains a null required section.");
                return errors;
            }

            ValidateTask(task, errors);
            if (errors.Count > 0)
            {
                continue;
            }

            var expected = aggregator.CreateTask(task.TaskId, task.Size, task.Outcome, task.ProtocolCompletion, task.ModelCallBudget,
                ReportAggregator.ToMeasurements(task), task.HelperResponses, task.Metrics.HelperCalls,
                new TaskSessionRecord(task.SessionId, task.SessionDirectory, task.ArtifactsAtStart, task.ArtifactsAtEnd),
                new AnswerScoringResult { Checks = task.Checks, Probe = task.Probe })
                with { RepositoryCommit = task.RepositoryCommit };
            if (!JsonElement.DeepEquals(JsonSerializer.SerializeToElement(task, ReportJson.Options),
                JsonSerializer.SerializeToElement(expected, ReportJson.Options)))
            {
                errors.Add($"Task '{task.TaskId}' has inconsistent metrics or turn mapping.");
            }
        }

        if (errors.Count == 0)
        {
            var expected = aggregator.CreateReport(report.Run, report.Tasks, report.UnrunTaskIds, report.Partial);
            if (!JsonElement.DeepEquals(JsonSerializer.SerializeToElement(report.Totals, ReportJson.Options),
                JsonSerializer.SerializeToElement(expected.Totals, ReportJson.Options)))
            {
                errors.Add("Report totals are inconsistent with task measurements.");
            }

            if (report.Partial != expected.Partial)
            {
                errors.Add("Report partial status is inconsistent with task outcomes or unrun tasks.");
            }
        }

        if (!report.Validation.IsValid || report.Validation.Errors is null || report.Validation.Errors.Count > 0)
        {
            errors.Add("Report declares invalid measurements.");
        }

        if (report.Run.Arm == "control" && report.Partial)
        {
            errors.Add("Control report must have complete run coverage.");
        }

        return errors;
    }

    /// <summary>
    ///     Checks the reproducibility metadata and collection invariants.
    /// </summary>
    /// <param name="metadata">The run provenance and effective settings. Cannot be <see langword="null" />.</param>
    /// <param name="tasks">The started task reports. Cannot be <see langword="null" />.</param>
    /// <param name="unrunTaskIds">The identifiers of manifest tasks that never started. Cannot be <see langword="null" />.</param>
    /// <returns>The collection invariant errors, or an empty list when collection is valid.</returns>
    internal static List<string> GetCollectionErrors(RunMetadata metadata, IReadOnlyList<TaskReport> tasks, IReadOnlyList<string> unrunTaskIds)
    {
        var errors = new List<string>();
        if (metadata.Arm is not ("treatment" or "control"))
        {
            errors.Add("Run arm must be treatment or control.");
        }

        if (string.IsNullOrWhiteSpace(metadata.RunId) || string.IsNullOrWhiteSpace(metadata.CommitSha)
            || metadata.EffectiveSettings is null || metadata.TurnBudgets is null
            || string.IsNullOrWhiteSpace(metadata.HelperModel) || string.IsNullOrWhiteSpace(metadata.ManifestPath)
            || metadata.ManifestSha256 is null || metadata.ManifestSha256.Length != 64 || !metadata.ManifestSha256.All(Uri.IsHexDigit)
            || metadata.ManifestProvenance is not ("file" or "inline"))
        {
            errors.Add("Run provenance or settings are missing or invalid.");
        }

        if (IsAbsolutePath(metadata.ManifestPath))
        {
            errors.Add("Run manifest path must be relative to the run folder.");
        }

        if (metadata.StartedAtUtc.Offset != TimeSpan.Zero || metadata.EndedAtUtc.Offset != TimeSpan.Zero
            || metadata.EndedAtUtc < metadata.StartedAtUtc || !double.IsFinite(metadata.HelperTemperature))
        {
            errors.Add("Run times must be ordered UTC values and generation settings must be finite.");
        }

        var settings = metadata.EffectiveSettings;
        if (settings is not null && (string.IsNullOrWhiteSpace(settings.AgentModel) || string.IsNullOrWhiteSpace(settings.SummarizerModel)
            || settings.MaxOutputTokens <= 0 || settings.ContextWindowTokens <= 0 || settings.ExchangeMaxTurns <= 0
            || !double.IsFinite(settings.SoftThresholdRatio) || !double.IsFinite(settings.HardThresholdRatio)
            || settings.SoftThresholdRatio is < 0 or > 1 || settings.HardThresholdRatio is < 0 or > 1
            || settings.HardThresholdRatio < settings.SoftThresholdRatio || settings.WindowSize < 0 || settings.SummaryWindowSize < 0
            || settings.MinSummaryTokens < 0 || settings.MaxSummaryTokens < settings.MinSummaryTokens
            || string.IsNullOrWhiteSpace(settings.TokenGuardLogLevel)))
        {
            errors.Add("Effective model, budget, summarization, or generation settings are invalid or nonfinite.");
        }

        if (metadata.HelperMaxOutputTokens <= 0 || metadata.HelperTemperature < 0 || metadata.TurnBudgets is not null
            && new[] { metadata.TurnBudgets.Small, metadata.TurnBudgets.Medium, metadata.TurnBudgets.Large }.Any(budget => budget is null
                || budget.MaxTurns <= 0 || budget.WrapUpWindow < 0 || budget.WrapUpWindow > budget.MaxTurns))
        {
            errors.Add("Helper generation settings or task turn budgets are invalid.");
        }

        if (tasks.Any(task => task is null || string.IsNullOrWhiteSpace(task.TaskId))
            || tasks.Select(task => task.TaskId).Distinct(StringComparer.Ordinal).Count() != tasks.Count
            || unrunTaskIds.Any(string.IsNullOrWhiteSpace) || unrunTaskIds.Distinct(StringComparer.Ordinal).Count() != unrunTaskIds.Count
            || tasks.Any(task => unrunTaskIds.Contains(task.TaskId, StringComparer.Ordinal)))
        {
            errors.Add("Task IDs must be present, unique, and disjoint from unrun task IDs.");
            return errors;
        }

        foreach (var task in tasks)
        {
            if (task.SummaryCrossCheck == "mismatched")
            {
                errors.Add($"Task '{task.TaskId}' summary cross-check failed.");
            }

            if (metadata.Arm == "control" && (!task.MeasurementsComplete || task.PrepareRecords is null || task.ProviderCalls is null
                || task.Metrics is null || task.PrepareRecords.Count == 0 || task.PrepareRecords.Any(prepare => prepare is null
                    || prepare.Status != "completed" || prepare.Outcome != "Ready" || prepare.TokensBefore is null || prepare.TokensAfter is null)
                || task.Metrics.StrategyRuns != 0 || task.Metrics.SummarizerCalls != 0
                || task.ProviderCalls.Any(call => !task.PrepareRecords.Any(prepare => prepare.Index == call.PrepareIndex))))
            {
                errors.Add($"Control task '{task.TaskId}' requires complete Ready prepare coverage with no strategy or summarizer runs.");
            }
        }

        if (metadata.Arm == "control" && (unrunTaskIds.Count > 0 || tasks.Count == 0))
        {
            errors.Add("Control report requires complete task coverage.");
        }

        return errors;
    }

    private static void ValidateTask(TaskReport task, List<string> errors)
    {
        ValidateScoring(task, errors);

        if (HasNonfiniteMetric(task.Metrics))
        {
            errors.Add($"Task '{task.TaskId}' contains nonfinite metrics.");
        }

        if (string.IsNullOrWhiteSpace(task.Size) || string.IsNullOrWhiteSpace(task.Outcome) || task.ModelCallBudget <= 0
            || task.DeliverableCompletion is not ("notEvaluated" or "complete" or "incomplete")
            || task.SummaryCrossCheck is not ("pending" or "matched" or "mismatched" or "unavailable")
            || (task.ProtocolCompletion && task.Outcome != "reply_received"))
        {
            errors.Add($"Task '{task.TaskId}' has invalid task metadata.");
        }

        if (IsAbsolutePath(task.SessionDirectory) || task.ArtifactsAtStart.Any(path => string.IsNullOrWhiteSpace(path) || IsAbsolutePath(path))
            || task.ArtifactsAtEnd.Any(file => file is null || string.IsNullOrWhiteSpace(file.Path) || IsAbsolutePath(file.Path)
                || file.SizeBytes < 0))
        {
            errors.Add($"Task '{task.TaskId}' has an absolute session path or invalid artifact records.");
        }

        if (task.PrepareRecords.Any(prepare => prepare is null || prepare.Index <= 0 || prepare.Status is not ("completed" or "incomplete")
            || prepare.MessagesCompacted < 0 || prepare.StrategyRuns < 0
            || (prepare.Status == "completed" && (string.IsNullOrWhiteSpace(prepare.Outcome) || prepare.TokensBefore is null
                || prepare.TokensAfter is null || prepare.OpeningMessagePresent is null || prepare.TokensBefore < 0 || prepare.TokensAfter < 0))
            || (prepare.Status == "incomplete"
                && (prepare.Outcome is not null || prepare.TokensBefore is not null || prepare.TokensAfter is not null
                    || prepare.OpeningMessagePresent is not null)))
            || task.PrepareRecords.Select(prepare => prepare.Index).Distinct().Count() != task.PrepareRecords.Count)
        {
            errors.Add($"Task '{task.TaskId}' has invalid prepare records.");
        }

        if (task.ProviderCalls.Any(call => call is null || call.TranscriptIndex < 0 || call.Status is not ("completed" or "failed" or "cancelled")
            || call.InputTokens < 0 || call.OutputTokens < 0 || !task.PrepareRecords.Any(prepare => prepare.Index == call.PrepareIndex))
            || task.ProviderCalls.Select(call => call.TranscriptIndex).Distinct().Count() != task.ProviderCalls.Count
            || task.ProviderCalls.Select(call => call.PrepareIndex).Distinct().Count() != task.ProviderCalls.Count)
        {
            errors.Add($"Task '{task.TaskId}' has invalid provider call records.");
        }

        if (task.SummarizerResponses.Any(usage => usage is null || usage.InputTokens < 0 || usage.OutputTokens < 0)
            || task.HelperResponses.Any(usage => usage is null || usage.InputTokens < 0 || usage.OutputTokens < 0)
            || task.Metrics.SummarizerCalls < task.SummarizerResponses.Count || task.Metrics.HelperCalls < task.HelperResponses.Count
            || task.Metrics.SummarizerFailures < 0 || task.Metrics.SummarizerFailures > task.Metrics.SummarizerCalls
            || task.Metrics.MessagesMasked < 0 || task.Metrics.MessagesSummarized < 0
            || task.Metrics.MessagesDropped < 0 || task.Metrics.SummarizationErrors < 0 || task.Metrics.EmergencyTruncations < 0
            || task.Metrics.HealthSignalCounts is null
            || task.Metrics.HealthSignalCounts.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value < 0))
        {
            errors.Add($"Task '{task.TaskId}' has invalid usage or counter measurements.");
        }
    }

    /// <summary>Checks recorded verdict contracts and shared eligibility without reconstructing excluded answer text.</summary>
    /// <param name="task">The task with non-null required collections.</param>
    /// <param name="errors">The destination for observable inconsistencies.</param>
    private static void ValidateScoring(TaskReport task, List<string> errors)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var check in task.Checks)
        {
            if (check is null || string.IsNullOrWhiteSpace(check.Id) || !char.IsAsciiLetterOrDigit(check.Id[0])
                || !check.Id.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_') || !ids.Add(check.Id)
                || (check.Passed ? check.Reason is not null
                    : check.Reason is not ("noAnswer" or "artifactMissing" or "notFound" or "forbiddenValuePresent")))
                errors.Add($"Task '{task.TaskId}' has invalid check results.");
        }
        if (task.Probe is not { } probe)
            return;
        if (probe.Requires is not (null or "masked" or "summarized" or "dropped"))
            errors.Add($"Task '{task.TaskId}' has invalid probe requires.");
        var eligibility = task.PrepareRecords.Any(record => record is null) ? "instructionNotCompacted"
            : ProbeValidity.GetInvalidReason(probe.Requires, ReportAggregator.ToMeasurements(task));
        var valid = probe.Status switch
        {
            "passed" => task.ProtocolCompletion && eligibility is null && probe.Reason is null && probe.CanaryPresent,
            "failed" => task.ProtocolCompletion && eligibility is null && probe.Reason == "canaryMissing" && !probe.CanaryPresent,
            "invalid" => probe.Reason switch
            {
                "noAnswer" => !probe.CanaryPresent,
                "canaryRepeated" => task.ProtocolCompletion,
                "instructionNotCompacted" or "requiredKindAbsent" => task.ProtocolCompletion && probe.Reason == eligibility,
                _ => false,
            },
            _ => false,
        };
        if (!valid || (!task.ProtocolCompletion && (probe.Status != "invalid" || probe.Reason != "noAnswer" || probe.CanaryPresent)))
            errors.Add($"Task '{task.TaskId}' has an inconsistent probe verdict.");
    }

    /// <summary>
    ///     Recognizes a rooted path in either Unix or Windows form, so a report validates the same on every machine.
    /// </summary>
    /// <param name="path">The recorded path, or <see langword="null" /> when none was recorded.</param>
    /// <returns><see langword="true" /> if the path is absolute; otherwise, <see langword="false" />.</returns>
    private static bool IsAbsolutePath(string? path)
    {
        return path is not null && (path.StartsWith('/') || path.StartsWith('\\') || Path.IsPathRooted(path)
            || (path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':'));
    }

    private static bool HasNonfiniteMetric(ReportMetrics metrics)
    {
        return typeof(ReportMetrics).GetProperties().Where(property => property.PropertyType == typeof(double?))
            .Any(property => property.GetValue(metrics) is double value && !double.IsFinite(value));
    }
}
