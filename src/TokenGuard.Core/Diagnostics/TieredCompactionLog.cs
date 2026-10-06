using Microsoft.Extensions.Logging;
using TokenGuard.Core.Strategies;

namespace TokenGuard.Core.Diagnostics;

/// <summary>
///     Provides the log messages written by <see cref="TieredCompactionStrategy" />.
/// </summary>
/// <remarks>
///     Event IDs 4000 to 4999 belong to tiered compaction.
/// </remarks>
internal static partial class TieredCompactionLog
{
    /// <summary>
    ///     The result name logged when the sliding-window result is returned.
    /// </summary>
    internal const string SlidingWindowResult = "SlidingWindow";

    /// <summary>
    ///     The result name logged when the summarization result is returned.
    /// </summary>
    internal const string SummarizationResult = "Summarization";

    /// <summary>
    ///     The reason logged when masking alone fits the budget.
    /// </summary>
    internal const string SlidingWindowSufficient = "SlidingWindowSufficient";

    /// <summary>
    ///     The reason logged when masking exceeds the budget and no summarizer is configured.
    /// </summary>
    internal const string NoSummarizerConfigured = "NoSummarizerConfigured";

    /// <summary>
    ///     The reason logged when summarization fits the budget.
    /// </summary>
    internal const string SummarizationSucceeded = "SummarizationSucceeded";

    /// <summary>
    ///     The reason logged when the summarization result still exceeds the budget.
    /// </summary>
    internal const string SummarizationOvershot = "SummarizationOvershot";

    /// <summary>
    ///     The reason logged when the summarization stage threw.
    /// </summary>
    internal const string SummarizationThrew = "SummarizationThrew";

    /// <summary>
    ///     Logs which stage produced the result of a tiered compaction call and why.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="result">The name of the stage whose result is returned.</param>
    /// <param name="reason">The name of the reason that stage was chosen.</param>
    /// <param name="slidingWindowTokens">The token total after sliding-window masking.</param>
    /// <param name="returnedTokens">The token total of the returned result.</param>
    /// <param name="availableTokens">The token budget available to the compacted result.</param>
    [LoggerMessage(
        EventId = 4000,
        EventName = "TieredResultSelected",
        Level = LogLevel.Debug,
        Message = "Tiered compaction returned the {Result} result ({Reason}): sliding window left {SlidingWindowTokens} tokens, the "
            + "returned result holds {ReturnedTokens} of {AvailableTokens} available tokens.")]
    internal static partial void TieredResultSelected(
        ILogger logger, string result, string reason, int slidingWindowTokens, int returnedTokens, int availableTokens);
}
