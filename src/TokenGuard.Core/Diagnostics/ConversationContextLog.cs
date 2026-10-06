using Microsoft.Extensions.Logging;
using TokenGuard.Core.Enums;

namespace TokenGuard.Core.Diagnostics;

/// <summary>
///     Provides the log messages written by <see cref="ConversationContext" />.
/// </summary>
/// <remarks>
///     Event IDs 1000 to 1999 belong to the conversation-context lifecycle. Messages carry counts and identifiers only,
///     never conversation content.
/// </remarks>
internal static partial class ConversationContextLog
{
    private static readonly Func<ILogger, string, string, int, IDisposable?> CompactionScope =
        LoggerMessage.DefineScope<string, string, int>("Conversation {ConversationId} ({ContextName}) turn {Turn}");

    /// <summary>
    ///     Begins the scope that tags every record written while the compaction strategy runs.
    /// </summary>
    /// <param name="logger">The logger to begin the scope on.</param>
    /// <param name="conversationId">The identifier of the conversation.</param>
    /// <param name="contextName">The configuration name of the conversation.</param>
    /// <param name="turn">The turn number of the prepare call.</param>
    /// <returns>The scope to dispose when the strategy returns, or <see langword="null" /> when the logger has no scope support.</returns>
    internal static IDisposable? BeginCompactionScope(ILogger logger, string conversationId, string contextName, int turn) =>
        CompactionScope(logger, conversationId, contextName, turn);

    /// <summary>
    ///     Logs that a message was recorded in the conversation history.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="conversationId">The identifier of the conversation.</param>
    /// <param name="contextName">The configuration name of the conversation.</param>
    /// <param name="role">The role of the recorded message.</param>
    /// <param name="isPinned">Whether the recorded message is pinned.</param>
    /// <param name="segmentCount">The number of content segments in the recorded message.</param>
    [LoggerMessage(
        EventId = 1000,
        EventName = "MessageRecorded",
        Level = LogLevel.Trace,
        Message = "Conversation {ConversationId} ({ContextName}) recorded a {Role} message: pinned {IsPinned}, {SegmentCount} segments.")]
    internal static partial void MessageRecorded(
        ILogger logger, string conversationId, string contextName, MessageRole role, bool isPinned, int segmentCount);

    /// <summary>
    ///     Logs the correction applied after the provider reported its input token count.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="conversationId">The identifier of the conversation.</param>
    /// <param name="contextName">The configuration name of the conversation.</param>
    /// <param name="providerInputTokens">The input token count reported by the provider.</param>
    /// <param name="lastEstimatedTokens">The token estimate of the most recently prepared payload.</param>
    /// <param name="correction">The signed correction added to later estimates.</param>
    [LoggerMessage(
        EventId = 1001,
        EventName = "EstimateAnchored",
        Level = LogLevel.Debug,
        Message = "Conversation {ConversationId} ({ContextName}) anchored its estimate: provider reported {ProviderInputTokens} input tokens, "
            + "last estimate was {LastEstimatedTokens}, correction {Correction}.")]
    internal static partial void EstimateAnchored(
        ILogger logger, string conversationId, string contextName, int providerInputTokens, int lastEstimatedTokens, int correction);

    /// <summary>
    ///     Logs a prepare call that returned the history because it is below the compaction trigger.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="conversationId">The identifier of the conversation.</param>
    /// <param name="contextName">The configuration name of the conversation.</param>
    /// <param name="turn">The turn number of the prepare call.</param>
    /// <param name="totalTokens">The estimated token total of the history.</param>
    /// <param name="triggerTokens">The token total at which compaction starts.</param>
    /// <param name="maxTokens">The configured maximum token count.</param>
    [LoggerMessage(
        EventId = 1010,
        EventName = "PrepareBelowTrigger",
        Level = LogLevel.Debug,
        Message = "Conversation {ConversationId} ({ContextName}) turn {Turn}: {TotalTokens} tokens is below the compaction trigger of "
            + "{TriggerTokens} (max {MaxTokens}).")]
    internal static partial void PrepareBelowTrigger(
        ILogger logger, string conversationId, string contextName, int turn, int totalTokens, int triggerTokens, int maxTokens);

    /// <summary>
    ///     Logs the result of a prepare call that ran the compaction strategy.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="conversationId">The identifier of the conversation.</param>
    /// <param name="contextName">The configuration name of the conversation.</param>
    /// <param name="turn">The turn number of the prepare call.</param>
    /// <param name="outcome">The outcome of the prepare call.</param>
    /// <param name="tokensBefore">The estimated token total before compaction.</param>
    /// <param name="tokensAfter">The estimated token total of the prepared payload.</param>
    /// <param name="messagesCompacted">The number of messages compacted or dropped.</param>
    /// <param name="emergencyMessagesDropped">The number of messages dropped by emergency truncation.</param>
    /// <param name="strategyName">The name reported by the compaction strategy.</param>
    /// <param name="elapsedMilliseconds">The duration of the prepare call in milliseconds.</param>
    [LoggerMessage(
        EventId = 1011,
        EventName = "CompactionCompleted",
        Level = LogLevel.Information,
        Message = "Conversation {ConversationId} ({ContextName}) turn {Turn}: compaction finished with outcome {Outcome}, {TokensBefore} -> "
            + "{TokensAfter} tokens, {MessagesCompacted} messages compacted, {EmergencyMessagesDropped} dropped by emergency truncation, "
            + "strategy {StrategyName}, {ElapsedMilliseconds:F1} ms.")]
    internal static partial void CompactionCompleted(
        ILogger logger, string conversationId, string contextName, int turn, PrepareOutcome outcome, int tokensBefore, int tokensAfter,
        int messagesCompacted, int emergencyMessagesDropped, string strategyName, double elapsedMilliseconds);

    /// <summary>
    ///     Logs that emergency truncation removed messages from the prepared payload.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="conversationId">The identifier of the conversation.</param>
    /// <param name="contextName">The configuration name of the conversation.</param>
    /// <param name="turn">The turn number of the prepare call.</param>
    /// <param name="messagesDropped">The number of messages removed.</param>
    /// <param name="tokensBefore">The estimated token total before truncation.</param>
    /// <param name="tokensAfter">The estimated token total after truncation.</param>
    [LoggerMessage(
        EventId = 1012,
        EventName = "EmergencyTruncationApplied",
        Level = LogLevel.Warning,
        Message = "Conversation {ConversationId} ({ContextName}) turn {Turn}: emergency truncation dropped {MessagesDropped} messages, "
            + "{TokensBefore} -> {TokensAfter} tokens.")]
    internal static partial void EmergencyTruncationApplied(
        ILogger logger, string conversationId, string contextName, int turn, int messagesDropped, int tokensBefore, int tokensAfter);

    /// <summary>
    ///     Logs that the compaction strategy reported a summarization failure.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="exception">The exception reported by the compaction strategy.</param>
    /// <param name="conversationId">The identifier of the conversation.</param>
    /// <param name="contextName">The configuration name of the conversation.</param>
    /// <param name="turn">The turn number of the prepare call.</param>
    /// <param name="strategyName">The name reported by the compaction strategy.</param>
    [LoggerMessage(
        EventId = 1013,
        EventName = "SummarizationFailed",
        Level = LogLevel.Warning,
        Message = "Conversation {ConversationId} ({ContextName}) turn {Turn}: summarization failed in strategy {StrategyName}; "
            + "the result without a summary was used.")]
    internal static partial void SummarizationFailed(
        ILogger logger, Exception exception, string conversationId, string contextName, int turn, string strategyName);

    /// <summary>
    ///     Logs that the prepared payload exceeds the budget.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="conversationId">The identifier of the conversation.</param>
    /// <param name="contextName">The configuration name of the conversation.</param>
    /// <param name="turn">The turn number of the prepare call.</param>
    /// <param name="outcome">The over-budget outcome of the prepare call.</param>
    /// <param name="finalTokens">The estimated token total of the prepared payload.</param>
    /// <param name="effectiveMaxTokens">The maximum token count plus the overrun tolerance.</param>
    [LoggerMessage(
        EventId = 1014,
        EventName = "PrepareOverBudget",
        Level = LogLevel.Error,
        Message = "Conversation {ConversationId} ({ContextName}) turn {Turn}: prepared payload is over budget with outcome {Outcome}, "
            + "{FinalTokens} tokens exceeds the effective maximum of {EffectiveMaxTokens}.")]
    internal static partial void PrepareOverBudget(
        ILogger logger, string conversationId, string contextName, int turn, PrepareOutcome outcome, int finalTokens, long effectiveMaxTokens);

    /// <summary>
    ///     Logs that pinned messages alone exceed the maximum token count.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="conversationId">The identifier of the conversation.</param>
    /// <param name="contextName">The configuration name of the conversation.</param>
    /// <param name="turn">The turn number of the prepare call.</param>
    /// <param name="pinnedTokens">The token total of all pinned messages.</param>
    /// <param name="maxTokens">The configured maximum token count.</param>
    [LoggerMessage(
        EventId = 1015,
        EventName = "PinnedBudgetExceeded",
        Level = LogLevel.Error,
        Message = "Conversation {ConversationId} ({ContextName}) turn {Turn}: pinned messages use {PinnedTokens} tokens, "
            + "above the maximum of {MaxTokens}.")]
    internal static partial void PinnedBudgetExceeded(
        ILogger logger, string conversationId, string contextName, int turn, int pinnedTokens, int maxTokens);

    /// <summary>
    ///     Logs how emergency truncation evaluated a prepared payload that exceeds the emergency trigger.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="conversationId">The identifier of the conversation.</param>
    /// <param name="contextName">The configuration name of the conversation.</param>
    /// <param name="turn">The turn number of the prepare call.</param>
    /// <param name="currentTokens">The estimated token total before truncation.</param>
    /// <param name="emergencyTriggerTokens">The token total above which truncation starts.</param>
    /// <param name="turnGroups">The number of turn groups that could be dropped.</param>
    /// <param name="turnGroupsDropped">The number of turn groups dropped.</param>
    /// <param name="preservedFloorIndex">The index of the first message that is always kept.</param>
    /// <param name="floorExceedsTrigger">Whether the kept messages alone still exceed the trigger.</param>
    [LoggerMessage(
        EventId = 1016,
        EventName = "EmergencyTruncationEvaluated",
        Level = LogLevel.Debug,
        Message = "Conversation {ConversationId} ({ContextName}) turn {Turn}: emergency truncation evaluated {CurrentTokens} tokens against "
            + "a trigger of {EmergencyTriggerTokens}: {TurnGroups} turn groups considered, {TurnGroupsDropped} dropped, preserved floor "
            + "starts at index {PreservedFloorIndex}, floor still exceeds the trigger: {FloorExceedsTrigger}.")]
    internal static partial void EmergencyTruncationEvaluated(
        ILogger logger, string conversationId, string contextName, int turn, int currentTokens, int emergencyTriggerTokens, int turnGroups,
        int turnGroupsDropped, int preservedFloorIndex, bool floorExceedsTrigger);
}
