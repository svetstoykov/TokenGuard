using Microsoft.Extensions.Logging;
using TokenGuard.Core.Strategies;

namespace TokenGuard.Core.Diagnostics;

/// <summary>
///     Provides the log messages written by <see cref="LlmSummarizationStrategy" />.
/// </summary>
/// <remarks>
///     Event IDs 3000 to 3999 belong to LLM summarization. Messages carry counts and token totals only, never summary
///     text or message content.
/// </remarks>
internal static partial class LlmSummarizationLog
{
    /// <summary>
    ///     The path name logged when a saved checkpoint matches the current history.
    /// </summary>
    internal const string WithCheckpointPath = "WithCheckpoint";

    /// <summary>
    ///     The path name logged when no saved checkpoint matches the current history.
    /// </summary>
    internal const string WithoutCheckpointPath = "WithoutCheckpoint";

    /// <summary>
    ///     The reason logged when the history holds fewer messages than the checkpoint covers.
    /// </summary>
    internal const string HistoryShorterThanCheckpoint = "HistoryShorterThanCheckpoint";

    /// <summary>
    ///     The reason logged when the messages covered by the checkpoint differ from the ones it was built from.
    /// </summary>
    internal const string SummarizedPrefixChanged = "SummarizedPrefixChanged";

    /// <summary>
    ///     Logs which summarization path a compaction call takes.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="path">The name of the path.</param>
    /// <param name="tailFirstIndex">The index of the first message kept unchanged.</param>
    /// <param name="tailMessages">The number of messages kept unchanged.</param>
    /// <param name="summarizableMessages">The number of messages older than the protected tail.</param>
    /// <param name="summarizableTokens">The token total of the messages older than the protected tail.</param>
    /// <param name="targetTokens">The summary size the path requests from the summarizer.</param>
    [LoggerMessage(
        EventId = 3000,
        EventName = "SummarizationPathSelected",
        Level = LogLevel.Debug,
        Message = "Summarization runs {Path}: protected tail starts at index {TailFirstIndex} with {TailMessages} messages, "
            + "{SummarizableMessages} older messages hold {SummarizableTokens} tokens, target summary size {TargetTokens} tokens.")]
    internal static partial void SummarizationPathSelected(
        ILogger logger, string path, int tailFirstIndex, int tailMessages, int summarizableMessages, int summarizableTokens, int targetTokens);

    /// <summary>
    ///     Logs that the history was returned unchanged because every message is inside the protected tail.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="messageCount">The number of messages passed to the strategy.</param>
    /// <param name="windowSize">The configured minimum number of protected messages.</param>
    [LoggerMessage(
        EventId = 3010,
        EventName = "SummarizationSkippedNothingToSummarize",
        Level = LogLevel.Debug,
        Message = "Summarization left the history unchanged: all {MessageCount} messages are inside the protected tail (window size "
            + "{WindowSize}).")]
    internal static partial void SummarizationSkippedNothingToSummarize(ILogger logger, int messageCount, int windowSize);

    /// <summary>
    ///     Logs that the history was returned unchanged because a summary covering more messages still exceeded the budget.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="tokensAfter">The token total of the summary plus the protected tail.</param>
    /// <param name="availableTokens">The token budget available to the compacted result.</param>
    /// <param name="summarizedMessages">The number of messages the rejected summary covered.</param>
    [LoggerMessage(
        EventId = 3011,
        EventName = "PromotedSummaryOvershot",
        Level = LogLevel.Debug,
        Message = "Summarization left the history unchanged: the summary extended to {SummarizedMessages} messages produced {TokensAfter} "
            + "tokens, above the {AvailableTokens} available.")]
    internal static partial void PromotedSummaryOvershot(ILogger logger, int tokensAfter, int availableTokens, int summarizedMessages);

    /// <summary>
    ///     Logs that the history was returned unchanged because a smaller rewrite of the saved summary still exceeded the budget.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="tokensAfter">The token total of the summary plus the protected tail.</param>
    /// <param name="availableTokens">The token budget available to the compacted result.</param>
    /// <param name="summarizedMessages">The number of messages the rejected summary covered.</param>
    [LoggerMessage(
        EventId = 3012,
        EventName = "RefreshedSummaryOvershot",
        Level = LogLevel.Debug,
        Message = "Summarization left the history unchanged: the rewritten summary of the same {SummarizedMessages} messages produced "
            + "{TokensAfter} tokens, above the {AvailableTokens} available.")]
    internal static partial void RefreshedSummaryOvershot(ILogger logger, int tokensAfter, int availableTokens, int summarizedMessages);

    /// <summary>
    ///     Logs that the history was returned unchanged because the room left for a first summary is below the minimum.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="remainingBudget">The tokens left after the protected tail.</param>
    /// <param name="minSummaryTokens">The configured minimum summary size.</param>
    /// <param name="availableTokens">The token budget available to the compacted result.</param>
    /// <param name="tailTokens">The token total of the protected tail.</param>
    [LoggerMessage(
        EventId = 3013,
        EventName = "SummarizationSkippedInsufficientBudget",
        Level = LogLevel.Debug,
        Message = "Summarization left the history unchanged: {RemainingBudget} tokens remain for a summary, below the minimum of "
            + "{MinSummaryTokens} ({AvailableTokens} available, {TailTokens} used by the protected tail).")]
    internal static partial void SummarizationSkippedInsufficientBudget(
        ILogger logger, int remainingBudget, int minSummaryTokens, int availableTokens, int tailTokens);

    /// <summary>
    ///     Logs that the history was returned unchanged because the first summary still exceeded the budget.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="tokensAfter">The token total of the summary plus the protected tail.</param>
    /// <param name="availableTokens">The token budget available to the compacted result.</param>
    /// <param name="summarizedMessages">The number of messages the rejected summary covered.</param>
    [LoggerMessage(
        EventId = 3014,
        EventName = "FirstSummaryOvershot",
        Level = LogLevel.Debug,
        Message = "Summarization left the history unchanged: the first summary of {SummarizedMessages} messages produced {TokensAfter} "
            + "tokens, above the {AvailableTokens} available.")]
    internal static partial void FirstSummaryOvershot(ILogger logger, int tokensAfter, int availableTokens, int summarizedMessages);

    /// <summary>
    ///     Logs that the history was returned unchanged, without a summarizer call, because the same attempt already failed.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="messageCount">The number of messages passed to the strategy.</param>
    /// <param name="availableTokens">The token budget available to the compacted result.</param>
    [LoggerMessage(
        EventId = 3015,
        EventName = "SummarizationSkippedRejectedAttempt",
        Level = LogLevel.Debug,
        Message = "Summarization left the history unchanged without calling the summarizer: an earlier attempt over the same "
            + "{MessageCount} messages with {AvailableTokens} available tokens threw or exceeded the budget.")]
    internal static partial void SummarizationSkippedRejectedAttempt(ILogger logger, int messageCount, int availableTokens);

    /// <summary>
    ///     Logs that a summary checkpoint was saved.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="summarizedMessages">The number of messages the checkpoint covers.</param>
    /// <param name="tokensAfter">The token total of the summary plus the protected tail.</param>
    [LoggerMessage(
        EventId = 3020,
        EventName = "SummaryCheckpointCreated",
        Level = LogLevel.Debug,
        Message = "Summary checkpoint created: it covers {SummarizedMessages} messages and the compacted result holds {TokensAfter} tokens.")]
    internal static partial void SummaryCheckpointCreated(ILogger logger, int summarizedMessages, int tokensAfter);

    /// <summary>
    ///     Logs that a saved summary checkpoint was reused without calling the summarizer.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="summarizedMessages">The number of messages the checkpoint covers.</param>
    /// <param name="tokensAfter">The token total of the summary plus the protected tail.</param>
    /// <param name="availableTokens">The token budget available to the compacted result.</param>
    [LoggerMessage(
        EventId = 3021,
        EventName = "SummaryCheckpointReused",
        Level = LogLevel.Debug,
        Message = "Summary checkpoint reused: it covers {SummarizedMessages} messages and the compacted result holds {TokensAfter} of "
            + "{AvailableTokens} available tokens.")]
    internal static partial void SummaryCheckpointReused(ILogger logger, int summarizedMessages, int tokensAfter, int availableTokens);

    /// <summary>
    ///     Logs that a saved summary checkpoint was discarded.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="reason">The name of the reason the checkpoint no longer matches the history.</param>
    /// <param name="messageCount">The number of messages passed to the strategy.</param>
    /// <param name="summarizedMessages">The number of messages the discarded checkpoint covered.</param>
    [LoggerMessage(
        EventId = 3022,
        EventName = "SummaryCheckpointCleared",
        Level = LogLevel.Debug,
        Message = "Summary checkpoint cleared ({Reason}): it covered {SummarizedMessages} messages and the history holds {MessageCount}.")]
    internal static partial void SummaryCheckpointCleared(ILogger logger, string reason, int messageCount, int summarizedMessages);
}
