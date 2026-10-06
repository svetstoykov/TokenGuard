namespace TokenGuard.Core.Defaults;

/// <summary>
///     Defines the thresholds at which a conversation context reports a health signal.
/// </summary>
internal static class ConversationHealthDefaults
{
    /// <summary>
    ///     The fraction of the provider-reported input tokens that the estimate may differ by before estimator drift is reported.
    /// </summary>
    internal const double EstimatorDriftRatio = 0.10;

    /// <summary>
    ///     The number of consecutive turns on which the strategy must run before repeated compaction is reported.
    /// </summary>
    internal const int RepeatedCompactionTurns = 3;

    /// <summary>
    ///     The fraction of the starting tokens that a strategy run must reclaim to avoid a low-yield report.
    /// </summary>
    internal const double LowYieldRatio = 0.05;

    /// <summary>
    ///     The number of consecutive strategy runs with a summarization failure before a failure streak is reported.
    /// </summary>
    internal const int SummarizationFailureStreak = 3;

    /// <summary>
    ///     The number of consecutive over-budget prepare calls before repeated over-budget is reported.
    /// </summary>
    internal const int RepeatedOverBudgetCalls = 2;

    /// <summary>
    ///     The fraction of the maximum token count that pinned messages may use before pinned pressure is reported.
    /// </summary>
    internal const double PinnedPressureRatio = 0.50;

    /// <summary>
    ///     The number of consecutive summarization runs that must clear and rebuild the checkpoint before churn is reported.
    /// </summary>
    internal const int CheckpointChurnRuns = 3;
}
