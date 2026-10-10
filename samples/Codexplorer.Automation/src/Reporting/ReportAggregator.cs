using Codexplorer.Measurements;
using Codexplorer.Automation.Scoring;

namespace Codexplorer.Automation.Reporting;

/// <summary>
///     Represents the pure task and run measurement aggregator.
/// </summary>
/// <remarks>This stateless service supports singleton registration and concurrent calls with immutable input snapshots.</remarks>
internal sealed class ReportAggregator : IReportAggregator
{
    /// <inheritdoc />
    public TaskReport CreateTask(string taskId, string size, string outcome, bool protocolCompletion, int budget,
        SessionMeasurements measurements, IReadOnlyList<UsageMeasurement> helperResponses, long helperCalls,
        TaskSessionRecord? session, AnswerScoringResult scoring)
    {
        ArgumentNullException.ThrowIfNull(measurements);
        var prepares = measurements.PrepareRecords.ToArray();
        var providers = measurements.ProviderCalls.ToArray();
        var offsets = providers.Select(call => prepares.SingleOrDefault(prepare => prepare.Index == call.PrepareIndex))
            .Zip(providers).Where(pair => pair.First?.Turn is not null)
            .Select(pair => pair.First!.Turn!.Value - pair.Second.TranscriptIndex).Distinct().ToArray();

        return new TaskReport
        {
            TaskId = taskId, Size = size, Outcome = outcome, ProtocolCompletion = protocolCompletion,
            Checks = scoring.Checks.ToArray(), Probe = scoring.Probe,
            DeliverableCompletion = scoring.Checks.Count == 0 ? "notEvaluated"
                : scoring.Checks.All(check => check.Passed) ? "complete" : "incomplete",
            ModelCallBudget = budget, MeasurementsComplete = measurements.Complete,
            SummaryCrossCheck = measurements.SummaryCrossCheck, SessionId = session?.SessionId,
            SessionDirectory = session?.SessionDirectory, ArtifactsAtStart = session?.ArtifactsAtStart.ToArray() ?? [],
            ArtifactsAtEnd = session?.ArtifactsAtEnd.ToArray() ?? [],
            TokenGuardTranscriptOffset = offsets.Length == 1 ? offsets[0] : null,
            PrepareRecords = prepares, ProviderCalls = providers, SummarizerResponses = measurements.SummarizerResponses.ToArray(),
            HelperResponses = helperResponses.ToArray(),
            Metrics = CalculateMetrics(prepares, providers, measurements.SummarizerResponses, helperResponses, helperCalls, budget,
                measurements, GetEstimatorErrors(prepares, providers)) with
            {
                ChecksTotal = scoring.Checks.Count, ChecksPassed = scoring.Checks.Count(check => check.Passed),
                CheckPassRate = scoring.Checks.Count == 0 ? null : (double)scoring.Checks.Count(check => check.Passed) / scoring.Checks.Count,
            }
        };
    }

    /// <inheritdoc />
    public RunReport CreateReport(RunMetadata metadata, IReadOnlyList<TaskReport> tasks, IReadOnlyList<string> unrunTaskIds, bool partial)
    {
        var snapshots = tasks.Select(ToMeasurements).ToArray();
        var merged = new SessionMeasurements
        {
            SummarizerCalls = snapshots.Sum(snapshot => snapshot.SummarizerCalls),
            SummarizerFailures = snapshots.Sum(snapshot => snapshot.SummarizerFailures),
            SummarizationErrors = snapshots.Sum(snapshot => snapshot.SummarizationErrors),
            MessagesDropped = snapshots.Sum(snapshot => snapshot.MessagesDropped),
            MessagesMasked = snapshots.Sum(snapshot => snapshot.MessagesMasked),
            MessagesSummarized = snapshots.Sum(snapshot => snapshot.MessagesSummarized),
            EditCallsSucceeded = snapshots.Sum(snapshot => snapshot.EditCallsSucceeded),
            EditCallsFailed = snapshots.Sum(snapshot => snapshot.EditCallsFailed),
            EmergencyTruncations = snapshots.Sum(snapshot => snapshot.EmergencyTruncations),
            HealthSignalCounts = MergeCounts(snapshots.Select(snapshot => snapshot.HealthSignalCounts))
        };
        var metrics = CalculateMetrics(tasks.SelectMany(task => task.PrepareRecords).ToArray(),
            tasks.SelectMany(task => task.ProviderCalls).ToArray(), tasks.SelectMany(task => task.SummarizerResponses).ToArray(),
            tasks.SelectMany(task => task.HelperResponses).ToArray(), tasks.Sum(task => task.Metrics.HelperCalls), 0, merged,
            tasks.SelectMany(task => GetEstimatorErrors(task.PrepareRecords, task.ProviderCalls)).ToArray()) with
        {
            BudgetOvershoot = tasks.Sum(task => task.Metrics.BudgetOvershoot),
            ChecksTotal = tasks.Sum(task => task.Metrics.ChecksTotal), ChecksPassed = tasks.Sum(task => task.Metrics.ChecksPassed),
            CheckPassRate = tasks.Sum(task => task.Metrics.ChecksTotal) == 0 ? null
                : (double)tasks.Sum(task => task.Metrics.ChecksPassed) / tasks.Sum(task => task.Metrics.ChecksTotal),
        };
        var errors = ReportValidator.GetCollectionErrors(metadata, tasks, unrunTaskIds);
        return new RunReport
        {
            SchemaVersion = 4, Run = metadata, Tasks = tasks.ToArray(), UnrunTaskIds = unrunTaskIds.ToArray(),
            Partial = partial || unrunTaskIds.Count > 0 || tasks.Any(task => task.Outcome is "failed" or "cancelled"),
            Totals = new RunTotals
            {
                TaskCount = tasks.Count, ProtocolCompletedTaskCount = tasks.Count(task => task.ProtocolCompletion),
                ProtocolCompletionRate = tasks.Count == 0 ? null : (double)tasks.Count(task => task.ProtocolCompletion) / tasks.Count,
                EvaluatedTaskCount = tasks.Count(task => task.Checks.Count > 0),
                DeliverableCompletedTaskCount = tasks.Count(task => task.DeliverableCompletion == "complete"),
                DeliverableCompletionRate = tasks.Count(task => task.Checks.Count > 0) == 0 ? null
                    : (double)tasks.Count(task => task.DeliverableCompletion == "complete") / tasks.Count(task => task.Checks.Count > 0),
                ProbeCount = tasks.Count(task => task.Probe is not null), InvalidProbeCount = tasks.Count(task => task.Probe?.Status == "invalid"),
                PassedProbeCount = tasks.Count(task => task.Probe?.Status == "passed"),
                CanaryPresentCount = tasks.Count(task => task.Probe?.CanaryPresent == true),
                ProbePassRate = tasks.Count(task => task.Probe is { Status: "passed" or "failed" }) == 0 ? null
                    : (double)tasks.Count(task => task.Probe?.Status == "passed")
                        / tasks.Count(task => task.Probe is { Status: "passed" or "failed" }),
                Metrics = metrics
            },
            Validation = new ReportValidation { IsValid = errors.Count == 0, Errors = errors }
        };
    }

    /// <summary>
    ///     Reconstructs the cumulative measurement snapshot represented by a task report.
    /// </summary>
    /// <param name="task">The task used by the scenario.</param>
    /// <returns>The recorded cumulative session measurement snapshot.</returns>
    internal static SessionMeasurements ToMeasurements(TaskReport task) => new()
    {
        PrepareRecords = task.PrepareRecords, ProviderCalls = task.ProviderCalls, SummarizerResponses = task.SummarizerResponses,
        SummarizerCalls = task.Metrics.SummarizerCalls, SummarizerFailures = task.Metrics.SummarizerFailures,
        SummarizationErrors = task.Metrics.SummarizationErrors, MessagesDropped = task.Metrics.MessagesDropped,
        MessagesMasked = task.Metrics.MessagesMasked, MessagesSummarized = task.Metrics.MessagesSummarized,
        EditCallsSucceeded = task.Metrics.EditCallsSucceeded, EditCallsFailed = task.Metrics.EditCallsFailed,
        EmergencyTruncations = task.Metrics.EmergencyTruncations, HealthSignalCounts = task.Metrics.HealthSignalCounts,
        SummaryCrossCheck = task.SummaryCrossCheck, Complete = task.MeasurementsComplete, ModelCallBudget = task.ModelCallBudget
    };

    /// <summary>
    ///     Aggregates counts, complete usage, and estimator statistics from one measurement population.
    /// </summary>
    /// <param name="prepares">Every prepare record in the population.</param>
    /// <param name="providers">Every started provider attempt.</param>
    /// <param name="summarizerResponses">Usage for received summarizer responses.</param>
    /// <param name="helperResponses">Usage for received helper responses.</param>
    /// <param name="helperCalls">The started helper attempts.</param>
    /// <param name="budget">The task allowance, or zero for run aggregation.</param>
    /// <param name="measurements">The independent telemetry counters.</param>
    /// <param name="errors">The eligible paired-turn error ratio population.</param>
    /// <returns>The aggregated numeric measurements.</returns>
    private static ReportMetrics CalculateMetrics(IReadOnlyList<PrepareMeasurement> prepares, IReadOnlyList<ProviderCallMeasurement> providers,
        IReadOnlyList<UsageMeasurement> summarizerResponses, IReadOnlyList<UsageMeasurement> helperResponses, long helperCalls, int budget,
        SessionMeasurements measurements, IReadOnlyList<double> errors)
    {
        var completed = prepares.Where(prepare => prepare.Status == "completed").ToArray();
        var before = completed.Sum(prepare => prepare.TokensBefore ?? 0);
        var after = completed.Sum(prepare => prepare.TokensAfter ?? 0);
        return new ReportMetrics
        {
            ChecksTotal = 0, ChecksPassed = 0, CheckPassRate = null,
            MessagesMasked = measurements.MessagesMasked, MessagesSummarized = measurements.MessagesSummarized,
            EditCallsSucceeded = measurements.EditCallsSucceeded, EditCallsFailed = measurements.EditCallsFailed,
            CompletedModelTurns = providers.Count(call => call.Status == "completed"), ModelCallsMade = providers.Count,
            BudgetOvershoot = Math.Max(0, providers.Count - budget), PrepareCalls = prepares.Count, CompletedPrepareCalls = completed.Length,
            StrategyRuns = completed.Sum(prepare => prepare.StrategyRuns), TokensBefore = before, TokensAfter = after,
            TokensReclaimed = completed.Where(prepare => prepare.StrategyRuns > 0)
                .Sum(prepare => (prepare.TokensBefore ?? 0) - (prepare.TokensAfter ?? 0)),
            PeakPreparedTokens = completed.Select(prepare => prepare.TokensAfter ?? 0).DefaultIfEmpty().Max(),
            ProviderInputTokens = SumUsage(providers.Select(call => call.InputTokens).ToArray(), providers.Count),
            ProviderOutputTokens = SumUsage(providers.Select(call => call.OutputTokens).ToArray(), providers.Count),
            ProviderMissingInputUsageCalls = providers.Count(call => call.InputTokens is null),
            ProviderMissingOutputUsageCalls = providers.Count(call => call.OutputTokens is null),
            MessagesCompacted = completed.Sum(prepare => prepare.MessagesCompacted), MessagesDropped = measurements.MessagesDropped,
            SummarizationErrors = measurements.SummarizationErrors, SummarizerCalls = measurements.SummarizerCalls,
            SummarizerFailures = measurements.SummarizerFailures,
            SummarizerInputTokens = SumUsage(summarizerResponses.Select(usage => usage.InputTokens).ToArray(), measurements.SummarizerCalls),
            SummarizerOutputTokens = SumUsage(summarizerResponses.Select(usage => usage.OutputTokens).ToArray(), measurements.SummarizerCalls),
            SummarizerMissingInputUsageCalls = measurements.SummarizerCalls - summarizerResponses.Count(usage => usage.InputTokens.HasValue),
            SummarizerMissingOutputUsageCalls = measurements.SummarizerCalls - summarizerResponses.Count(usage => usage.OutputTokens.HasValue),
            HelperCalls = helperCalls, HelperInputTokens = SumUsage(helperResponses.Select(usage => usage.InputTokens).ToArray(), helperCalls),
            HelperOutputTokens = SumUsage(helperResponses.Select(usage => usage.OutputTokens).ToArray(), helperCalls),
            HelperMissingInputUsageCalls = helperCalls - helperResponses.Count(usage => usage.InputTokens.HasValue),
            HelperMissingOutputUsageCalls = helperCalls - helperResponses.Count(usage => usage.OutputTokens.HasValue),
            EmergencyTruncations = measurements.EmergencyTruncations,
            EstimatedPromptTokenReduction = before == 0 ? null : (double)(before - after) / before, EstimatorPairedTurnCount = errors.Count,
            EstimatorSignedMean = errors.Count == 0 ? null : errors.Average(), EstimatorSignedP95 = P95(errors),
            EstimatorAbsoluteMean = errors.Count == 0 ? null : errors.Average(Math.Abs),
            EstimatorAbsoluteP95 = P95(errors.Select(Math.Abs).ToArray()),
            PrepareOutcomeCounts = completed.GroupBy(prepare => prepare.Outcome!).OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.LongCount()),
            HealthSignalCounts = MergeCounts([measurements.HealthSignalCounts])
        };
    }

    /// <summary>
    ///     Computes signed error ratios from completed prepare/provider pairs with positive reported input usage.
    /// </summary>
    /// <param name="prepares">The task prepare records.</param>
    /// <param name="providers">The task provider attempts with prepare mappings.</param>
    /// <returns>The paired error population; positive ratios mean the estimate was below provider usage.</returns>
    private static double[] GetEstimatorErrors(IReadOnlyList<PrepareMeasurement> prepares, IReadOnlyList<ProviderCallMeasurement> providers)
    {
        return providers.Where(call => call.Status == "completed" && call.InputTokens > 0)
            .Select(call => (Call: call, Prepare: prepares.SingleOrDefault(prepare => prepare.Index == call.PrepareIndex)))
            .Where(pair => pair.Prepare is { Status: "completed", TokensAfter: not null })
            .Select(pair => (pair.Call.InputTokens!.Value - (double)pair.Prepare!.TokensAfter!.Value) / pair.Call.InputTokens.Value).ToArray();
    }

    /// <summary>
    ///     Sums usage only when every attempted call has a reported measurement.
    /// </summary>
    /// <param name="usage">The received usage measurements.</param>
    /// <param name="expectedCalls">The number of attempted calls.</param>
    /// <returns>The known total, or <see langword="null" /> when any call lacks usage.</returns>
    private static long? SumUsage(IReadOnlyList<long?> usage, long expectedCalls)
    {
        return usage.Count == expectedCalls && usage.All(value => value.HasValue) ? usage.Sum(value => value!.Value) : null;
    }

    /// <summary>
    ///     Selects the nearest-rank 95th percentile of an observation population.
    /// </summary>
    /// <param name="values">The finite observations.</param>
    /// <returns>The percentile, or <see langword="null" /> for an empty population.</returns>
    private static double? P95(IReadOnlyList<double> values)
    {
        return values.Count == 0 ? null : values.Order().ElementAt((int)Math.Ceiling(values.Count * 0.95) - 1);
    }

    private static IReadOnlyDictionary<string, long> MergeCounts(IEnumerable<IReadOnlyDictionary<string, long>> dictionaries)
    {
        return dictionaries.SelectMany(dictionary => dictionary).GroupBy(pair => pair.Key).OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(pair => pair.Value));
    }
}
