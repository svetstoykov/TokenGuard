using Microsoft.Extensions.Logging;
using TokenGuard.Core.Abstractions;

namespace TokenGuard.Core.Diagnostics;

/// <summary>
///     Provides the log messages written by the provider implementations of <see cref="ILlmSummarizer" />.
/// </summary>
/// <remarks>
///     Event IDs 5000 to 5999 belong to provider summarizers. The catalog lives in <c>TokenGuard.Core</c> so that the
///     extension packages share one set of event IDs. Messages carry sizes and token counts only, never prompt or summary
///     text.
/// </remarks>
internal static partial class SummarizerLog
{
    /// <summary>
    ///     Logs that a provider summarization request is about to be sent.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="provider">The name of the provider.</param>
    /// <param name="model">The model the request targets, or <see langword="null" /> when the summarizer does not know it.</param>
    /// <param name="messageCount">The number of messages being summarized.</param>
    /// <param name="targetTokens">The requested summary size.</param>
    [LoggerMessage(
        EventId = 5000,
        EventName = "SummarizerCallStarting",
        Level = LogLevel.Debug,
        Message = "{Provider} summarizer call starting: {MessageCount} messages, target {TargetTokens} tokens, model {Model}.")]
    internal static partial void SummarizerCallStarting(ILogger logger, string provider, string? model, int messageCount, int targetTokens);

    /// <summary>
    ///     Logs that a provider summarization request returned a summary.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="provider">The name of the provider.</param>
    /// <param name="elapsedMilliseconds">The duration of the call in milliseconds.</param>
    /// <param name="summaryLength">The length of the summary in characters.</param>
    /// <param name="inputTokens">The input tokens reported by the provider, or <see langword="null" /> when not reported.</param>
    /// <param name="outputTokens">The output tokens reported by the provider, or <see langword="null" /> when not reported.</param>
    [LoggerMessage(
        EventId = 5001,
        EventName = "SummarizerCallCompleted",
        Level = LogLevel.Debug,
        Message = "{Provider} summarizer call completed in {ElapsedMilliseconds:F1} ms: summary of {SummaryLength} characters, provider "
            + "reported {InputTokens} input and {OutputTokens} output tokens.")]
    internal static partial void SummarizerCallCompleted(
        ILogger logger, string provider, double elapsedMilliseconds, int summaryLength, long? inputTokens, long? outputTokens);

    /// <summary>
    ///     Logs that a provider summarization request failed.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="provider">The name of the provider.</param>
    /// <param name="elapsedMilliseconds">The duration of the call in milliseconds.</param>
    /// <param name="exceptionType">The type name of the exception that ended the call.</param>
    [LoggerMessage(
        EventId = 5002,
        EventName = "SummarizerCallFailed",
        Level = LogLevel.Debug,
        Message = "{Provider} summarizer call failed after {ElapsedMilliseconds:F1} ms with {ExceptionType}.")]
    internal static partial void SummarizerCallFailed(ILogger logger, string provider, double elapsedMilliseconds, string exceptionType);
}
