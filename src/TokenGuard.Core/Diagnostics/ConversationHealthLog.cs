using Microsoft.Extensions.Logging;

namespace TokenGuard.Core.Diagnostics;

/// <summary>
///     Provides the log messages for conversation health signals and the end-of-conversation summary.
/// </summary>
/// <remarks>
///     Event IDs 6000 to 6999 belong to health signals. Each signal writes one record when its condition starts to hold
///     and one <see cref="HealthSignalCleared" /> record when it stops.
/// </remarks>
internal static partial class ConversationHealthLog
{
    /// <summary>
    ///     The signal name for a token estimate that differs too much from the provider-reported value.
    /// </summary>
    internal const string EstimatorDrift = "EstimatorDrift";

    /// <summary>
    ///     The signal name for a strategy that runs on several consecutive turns.
    /// </summary>
    internal const string RepeatedCompaction = "RepeatedCompaction";

    /// <summary>
    ///     The signal name for a strategy run that reclaims almost nothing.
    /// </summary>
    internal const string LowCompactionYield = "LowCompactionYield";

    /// <summary>
    ///     The signal name for summarization that fails on several consecutive strategy runs.
    /// </summary>
    internal const string SummarizationFailureStreak = "SummarizationFailureStreak";

    /// <summary>
    ///     The signal name for consecutive prepare calls that end over budget.
    /// </summary>
    internal const string RepeatedOverBudget = "RepeatedOverBudget";

    /// <summary>
    ///     The signal name for pinned messages that use a large share of the budget.
    /// </summary>
    internal const string PinnedPressure = "PinnedPressure";

    /// <summary>
    ///     The signal name for a summary checkpoint that is cleared and rebuilt on several consecutive runs.
    /// </summary>
    internal const string CheckpointChurn = "CheckpointChurn";

    /// <summary>
    ///     Logs that the token estimate started to drift from the provider-reported value.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="conversationId">The identifier of the conversation.</param>
    /// <param name="contextName">The configuration name of the conversation.</param>
    /// <param name="estimatedTokens">The token estimate of the most recently prepared payload.</param>
    /// <param name="providerInputTokens">The input token count reported by the provider.</param>
    /// <param name="driftPercent">The signed difference as a percentage of the provider-reported value.</param>
    [LoggerMessage(
        EventId = 6001,
        EventName = "EstimatorDriftDetected",
        Level = LogLevel.Warning,
        Message = "Conversation {ConversationId} ({ContextName}) health: estimator drift. Estimated {EstimatedTokens} tokens, provider "
            + "reported {ProviderInputTokens} ({DriftPercent:F1}%).")]
    internal static partial void EstimatorDriftDetected(
        ILogger logger, string conversationId, string contextName, int estimatedTokens, int providerInputTokens, double driftPercent);

    /// <summary>
    ///     Logs that the compaction strategy started to run on every turn.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="conversationId">The identifier of the conversation.</param>
    /// <param name="contextName">The configuration name of the conversation.</param>
    /// <param name="consecutiveTurns">The number of consecutive turns on which the strategy ran.</param>
    /// <param name="tokensReclaimedPerTurn">The tokens reclaimed on each of those turns, oldest first.</param>
    [LoggerMessage(
        EventId = 6002,
        EventName = "RepeatedCompactionDetected",
        Level = LogLevel.Warning,
        Message = "Conversation {ConversationId} ({ContextName}) health: repeated compaction. The strategy ran on {ConsecutiveTurns} "
            + "consecutive turns and reclaimed {TokensReclaimedPerTurn} tokens.")]
    internal static partial void RepeatedCompactionDetected(
        ILogger logger, string conversationId, string contextName, int consecutiveTurns, string tokensReclaimedPerTurn);

    /// <summary>
    ///     Logs that a strategy run started to reclaim almost nothing.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="conversationId">The identifier of the conversation.</param>
    /// <param name="contextName">The configuration name of the conversation.</param>
    /// <param name="tokensBefore">The estimated token total before compaction.</param>
    /// <param name="tokensAfter">The estimated token total of the prepared payload.</param>
    /// <param name="yieldPercent">The reclaimed tokens as a percentage of the tokens before compaction.</param>
    [LoggerMessage(
        EventId = 6003,
        EventName = "LowCompactionYieldDetected",
        Level = LogLevel.Warning,
        Message = "Conversation {ConversationId} ({ContextName}) health: low compaction yield. {TokensBefore} -> {TokensAfter} tokens "
            + "({YieldPercent:F1}% reclaimed).")]
    internal static partial void LowCompactionYieldDetected(
        ILogger logger, string conversationId, string contextName, int tokensBefore, int tokensAfter, double yieldPercent);

    /// <summary>
    ///     Logs that summarization started to fail on consecutive strategy runs.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="conversationId">The identifier of the conversation.</param>
    /// <param name="contextName">The configuration name of the conversation.</param>
    /// <param name="consecutiveFailures">The number of consecutive strategy runs that reported a summarization failure.</param>
    /// <param name="exceptionType">The type name of the most recent exception.</param>
    [LoggerMessage(
        EventId = 6004,
        EventName = "SummarizationFailureStreakDetected",
        Level = LogLevel.Error,
        Message = "Conversation {ConversationId} ({ContextName}) health: summarization failure streak. {ConsecutiveFailures} consecutive "
            + "strategy runs failed to summarize, most recently with {ExceptionType}.")]
    internal static partial void SummarizationFailureStreakDetected(
        ILogger logger, string conversationId, string contextName, int consecutiveFailures, string exceptionType);

    /// <summary>
    ///     Logs that consecutive prepare calls started to end over budget.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="conversationId">The identifier of the conversation.</param>
    /// <param name="contextName">The configuration name of the conversation.</param>
    /// <param name="consecutiveCalls">The number of consecutive over-budget prepare calls.</param>
    /// <param name="finalTokens">The estimated token total of the most recent prepared payload.</param>
    /// <param name="effectiveMaxTokens">The maximum token count plus the overrun tolerance.</param>
    [LoggerMessage(
        EventId = 6005,
        EventName = "RepeatedOverBudgetDetected",
        Level = LogLevel.Error,
        Message = "Conversation {ConversationId} ({ContextName}) health: repeated over-budget. {ConsecutiveCalls} consecutive prepare "
            + "calls ended over budget, most recently with {FinalTokens} tokens against an effective maximum of {EffectiveMaxTokens}.")]
    internal static partial void RepeatedOverBudgetDetected(
        ILogger logger, string conversationId, string contextName, int consecutiveCalls, int finalTokens, long effectiveMaxTokens);

    /// <summary>
    ///     Logs that pinned messages started to use a large share of the budget.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="conversationId">The identifier of the conversation.</param>
    /// <param name="contextName">The configuration name of the conversation.</param>
    /// <param name="pinnedTokens">The token total of all pinned messages.</param>
    /// <param name="maxTokens">The configured maximum token count.</param>
    /// <param name="pinnedPercent">The pinned tokens as a percentage of the maximum token count.</param>
    [LoggerMessage(
        EventId = 6006,
        EventName = "PinnedPressureDetected",
        Level = LogLevel.Warning,
        Message = "Conversation {ConversationId} ({ContextName}) health: pinned pressure. Pinned messages use {PinnedTokens} of "
            + "{MaxTokens} tokens ({PinnedPercent:F1}%).")]
    internal static partial void PinnedPressureDetected(
        ILogger logger, string conversationId, string contextName, int pinnedTokens, int maxTokens, double pinnedPercent);

    /// <summary>
    ///     Logs that the summary checkpoint started to be cleared and rebuilt on every summarization run.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="conversationId">The identifier of the conversation.</param>
    /// <param name="contextName">The configuration name of the conversation.</param>
    /// <param name="consecutiveRebuilds">The number of consecutive runs that cleared and rebuilt the checkpoint.</param>
    [LoggerMessage(
        EventId = 6007,
        EventName = "CheckpointChurnDetected",
        Level = LogLevel.Warning,
        Message = "Conversation {ConversationId} ({ContextName}) health: checkpoint churn. The summary checkpoint was cleared and "
            + "rebuilt on {ConsecutiveRebuilds} consecutive summarization runs.")]
    internal static partial void CheckpointChurnDetected(ILogger logger, string conversationId, string contextName, int consecutiveRebuilds);

    /// <summary>
    ///     Logs that the condition behind a health signal stopped holding.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="conversationId">The identifier of the conversation.</param>
    /// <param name="contextName">The configuration name of the conversation.</param>
    /// <param name="signal">The name of the signal.</param>
    [LoggerMessage(
        EventId = 6050,
        EventName = "HealthSignalCleared",
        Level = LogLevel.Information,
        Message = "Conversation {ConversationId} ({ContextName}) health: {Signal} cleared.")]
    internal static partial void HealthSignalCleared(ILogger logger, string conversationId, string contextName, string signal);

    /// <summary>
    ///     Logs the totals of a conversation when its context is disposed.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="conversationId">The identifier of the conversation.</param>
    /// <param name="contextName">The configuration name of the conversation.</param>
    /// <param name="turns">The number of turns the conversation reached.</param>
    /// <param name="prepareCalls">The number of prepare calls.</param>
    /// <param name="strategyRuns">The number of prepare calls that ran the compaction strategy.</param>
    /// <param name="tokensReclaimed">The total tokens reclaimed by compaction.</param>
    /// <param name="summarizerCalls">The number of summarizer calls.</param>
    /// <param name="summarizerFailures">The number of summarizer calls that threw.</param>
    /// <param name="emergencyTruncations">The number of prepare calls in which emergency truncation removed messages.</param>
    /// <param name="peakPreparedTokens">The largest estimated token total of a prepared payload.</param>
    /// <param name="largestDriftPercent">The largest absolute estimator drift seen, as a percentage.</param>
    [LoggerMessage(
        EventId = 6100,
        EventName = "ConversationSummary",
        Level = LogLevel.Information,
        Message = "Conversation {ConversationId} ({ContextName}) ended: {Turns} turns, {PrepareCalls} prepare calls, {StrategyRuns} "
            + "strategy runs, {TokensReclaimed} tokens reclaimed, {SummarizerCalls} summarizer calls ({SummarizerFailures} failed), "
            + "{EmergencyTruncations} emergency truncations, peak {PeakPreparedTokens} prepared tokens, largest estimator drift "
            + "{LargestDriftPercent:F1}%.")]
    internal static partial void ConversationSummary(
        ILogger logger, string conversationId, string contextName, int turns, int prepareCalls, int strategyRuns, long tokensReclaimed,
        int summarizerCalls, int summarizerFailures, int emergencyTruncations, int peakPreparedTokens, double largestDriftPercent);
}
