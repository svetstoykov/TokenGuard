namespace Codexplorer.Automation.Reporting;

/// <summary>
///     Represents task or run measurements and estimator statistics.
/// </summary>
/// <remarks>Nullable usage totals require usage for every attempted call. Run peak tokens use the maximum; additive counters use sums.</remarks>
internal sealed record ReportMetrics
{
    /// <summary>Gets the check population.</summary>
    public required long ChecksTotal { get; init; }

    /// <summary>Gets the passed check count.</summary>
    public required long ChecksPassed { get; init; }

    /// <summary>Gets the passed fraction, or null for no checks.</summary>
    public required double? CheckPassRate { get; init; }

    /// <summary>Gets the independently measured masked count.</summary>
    public required long MessagesMasked { get; init; }

    /// <summary>Gets the independently measured summarized count.</summary>
    public required long MessagesSummarized { get; init; }

    /// <summary>Gets the <c>edit_file</c> calls that changed a repository file.</summary>
    public required long EditCallsSucceeded { get; init; }

    /// <summary>Gets the <c>edit_file</c> calls that returned an error and left the file unchanged.</summary>
    public required long EditCallsFailed { get; init; }


    /// <summary>
    ///     Gets the number of completed provider calls.
    /// </summary>
    public required long CompletedModelTurns { get; init; }

    /// <summary>
    ///     Gets the number of started provider attempts.
    /// </summary>
    public required long ModelCallsMade { get; init; }

    /// <summary>
    ///     Gets the calls made beyond the task allowance, floored at zero.
    /// </summary>
    public required long BudgetOvershoot { get; init; }

    /// <summary>
    ///     Gets the number of attempted prepares, including incomplete attempts.
    /// </summary>
    public required long PrepareCalls { get; init; }

    /// <summary>
    ///     Gets the number of prepares carrying a completed outcome.
    /// </summary>
    public required long CompletedPrepareCalls { get; init; }

    /// <summary>
    ///     Gets the completed compaction strategy runs belonging to completed prepares.
    /// </summary>
    public required long StrategyRuns { get; init; }

    /// <summary>
    ///     Gets the sum of token estimates before completed prepares.
    /// </summary>
    public required long TokensBefore { get; init; }

    /// <summary>
    ///     Gets the sum of token estimates after completed prepares.
    /// </summary>
    public required long TokensAfter { get; init; }

    /// <summary>
    ///     Gets the signed tokens reclaimed by completed strategy runs.
    /// </summary>
    public required long TokensReclaimed { get; init; }

    /// <summary>
    ///     Gets the largest completed prepared token estimate across the measured population.
    /// </summary>
    public required long PeakPreparedTokens { get; init; }

    /// <summary>
    ///     Gets total provider input usage, or <see langword="null" /> when any attempt lacks usage.
    /// </summary>
    public required long? ProviderInputTokens { get; init; }

    /// <summary>
    ///     Gets total provider output usage, or <see langword="null" /> when any attempt lacks usage.
    /// </summary>
    public required long? ProviderOutputTokens { get; init; }

    /// <summary>
    ///     Gets the provider attempts lacking input usage.
    /// </summary>
    public required long ProviderMissingInputUsageCalls { get; init; }

    /// <summary>
    ///     Gets the provider attempts lacking output usage.
    /// </summary>
    public required long ProviderMissingOutputUsageCalls { get; init; }

    /// <summary>
    ///     Gets the messages compacted by completed prepares.
    /// </summary>
    public required long MessagesCompacted { get; init; }

    /// <summary>
    ///     Gets the messages dropped according to telemetry.
    /// </summary>
    public required long MessagesDropped { get; init; }

    /// <summary>
    ///     Gets the prepare results reporting a summarization error.
    /// </summary>
    public required long SummarizationErrors { get; init; }

    /// <summary>
    ///     Gets the attempted summarizer calls.
    /// </summary>
    public required long SummarizerCalls { get; init; }

    /// <summary>
    ///     Gets the failed summarizer calls, excluding caller cancellation.
    /// </summary>
    public required long SummarizerFailures { get; init; }

    /// <summary>
    ///     Gets total summarizer input usage, or <see langword="null" /> when any call lacks usage.
    /// </summary>
    public required long? SummarizerInputTokens { get; init; }

    /// <summary>
    ///     Gets total summarizer output usage, or <see langword="null" /> when any call lacks usage.
    /// </summary>
    public required long? SummarizerOutputTokens { get; init; }

    /// <summary>
    ///     Gets the summarizer calls lacking input usage.
    /// </summary>
    public required long SummarizerMissingInputUsageCalls { get; init; }

    /// <summary>
    ///     Gets the summarizer calls lacking output usage.
    /// </summary>
    public required long SummarizerMissingOutputUsageCalls { get; init; }

    /// <summary>
    ///     Gets the attempted helper calls.
    /// </summary>
    public required long HelperCalls { get; init; }

    /// <summary>
    ///     Gets total helper input usage, or <see langword="null" /> when any call lacks usage.
    /// </summary>
    public required long? HelperInputTokens { get; init; }

    /// <summary>
    ///     Gets total helper output usage, or <see langword="null" /> when any call lacks usage.
    /// </summary>
    public required long? HelperOutputTokens { get; init; }

    /// <summary>
    ///     Gets the helper calls lacking input usage.
    /// </summary>
    public required long HelperMissingInputUsageCalls { get; init; }

    /// <summary>
    ///     Gets the helper calls lacking output usage.
    /// </summary>
    public required long HelperMissingOutputUsageCalls { get; init; }

    /// <summary>
    ///     Gets the emergency truncation count.
    /// </summary>
    public required long EmergencyTruncations { get; init; }

    /// <summary>
    ///     Gets the reduction ratio over completed prepares, or <see langword="null" /> for a zero denominator.
    /// </summary>
    public required double? EstimatedPromptTokenReduction { get; init; }

    /// <summary>
    ///     Gets the completed prepare/provider pairs with positive reported input usage.
    /// </summary>
    public required long EstimatorPairedTurnCount { get; init; }

    /// <summary>
    ///     Gets the mean signed estimate error ratio, or <see langword="null" /> when the paired population is empty.
    /// </summary>
    public required double? EstimatorSignedMean { get; init; }

    /// <summary>
    ///     Gets the nearest-rank P95 signed estimate error ratio, or <see langword="null" /> when the paired population is empty.
    /// </summary>
    public required double? EstimatorSignedP95 { get; init; }

    /// <summary>
    ///     Gets the mean absolute estimate error ratio over the paired population.
    /// </summary>
    public required double? EstimatorAbsoluteMean { get; init; }

    /// <summary>
    ///     Gets the nearest-rank P95 absolute estimate error ratio over the paired population.
    /// </summary>
    public required double? EstimatorAbsoluteP95 { get; init; }

    /// <summary>
    ///     Gets the completed prepare outcome distribution.
    /// </summary>
    public required IReadOnlyDictionary<string, long> PrepareOutcomeCounts { get; init; }

    /// <summary>
    ///     Gets the health signal distribution.
    /// </summary>
    public required IReadOnlyDictionary<string, long> HealthSignalCounts { get; init; }
}
