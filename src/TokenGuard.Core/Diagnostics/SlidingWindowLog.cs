using Microsoft.Extensions.Logging;
using TokenGuard.Core.Strategies;

namespace TokenGuard.Core.Diagnostics;

/// <summary>
///     Provides the log messages written by <see cref="SlidingWindowStrategy" />.
/// </summary>
/// <remarks>
///     Event IDs 2000 to 2999 belong to sliding-window masking. Tool names and tool call identifiers are written as
///     metadata; tool arguments and tool results are never written.
/// </remarks>
internal static partial class SlidingWindowLog
{
    /// <summary>
    ///     Logs the result of one sliding-window pass.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="messageCount">The number of messages passed to the strategy.</param>
    /// <param name="availableTokens">The token budget available to the compacted result.</param>
    /// <param name="tokensBefore">The token total before masking.</param>
    /// <param name="tokensAfter">The token total after masking.</param>
    /// <param name="windowSize">The configured minimum number of protected messages.</param>
    /// <param name="protectedMessages">The number of newest messages left unchanged.</param>
    /// <param name="toolResultsMasked">The number of tool results replaced with placeholders.</param>
    [LoggerMessage(
        EventId = 2000,
        EventName = "SlidingWindowApplied",
        Level = LogLevel.Debug,
        Message = "Sliding window processed {MessageCount} messages with {AvailableTokens} available tokens: {TokensBefore} -> {TokensAfter} "
            + "tokens, window size {WindowSize}, {ProtectedMessages} protected messages, {ToolResultsMasked} tool results masked.")]
    internal static partial void SlidingWindowApplied(
        ILogger logger, int messageCount, int availableTokens, int tokensBefore, int tokensAfter, int windowSize, int protectedMessages,
        int toolResultsMasked);

    /// <summary>
    ///     Logs that one tool result was replaced with a placeholder.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="messageIndex">The index of the message that holds the tool result.</param>
    /// <param name="toolCallId">The identifier of the tool call.</param>
    /// <param name="toolName">The name of the tool.</param>
    /// <param name="tokensBefore">The token count of the message before masking.</param>
    /// <param name="tokensAfter">The token count of the message after masking.</param>
    [LoggerMessage(
        EventId = 2001,
        EventName = "ToolResultMasked",
        Level = LogLevel.Trace,
        Message = "Masked tool result {ToolCallId} of tool {ToolName} in message {MessageIndex}: message went from {TokensBefore} to "
            + "{TokensAfter} tokens.")]
    internal static partial void ToolResultMasked(
        ILogger logger, int messageIndex, string toolCallId, string toolName, int tokensBefore, int tokensAfter);
}
