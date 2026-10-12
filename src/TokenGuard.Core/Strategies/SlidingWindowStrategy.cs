using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Defaults;
using TokenGuard.Core.Diagnostics;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;
using TokenGuard.Core.Options;

namespace TokenGuard.Core.Strategies;

/// <summary>
///     Masks tool results outside a protected newest-message window.
/// </summary>
/// <remarks>
///     <para>
///         Use <see cref="SlidingWindowStrategy"/> when recent messages must remain fully intact for the active
///         agent turn, while older <see cref="ToolResultContent"/> payloads can be replaced with compact
///         placeholders to reduce retained context size.
///     </para>
///     <para>
///         The strategy always protects at least <see cref="SlidingWindowOptions.WindowSize"/> newest messages of the
///         compactable slice supplied by <c>CompactAsync</c>. Nothing is masked while the whole slice fits in
///         <see cref="SlidingWindowOptions.ProtectedWindowFraction"/> of <c>availableTokens</c>, the high mark. Above
///         it, the protected part holds between the high mark and one step below it, where a step is one quarter of
///         the high mark. Messages before the boundary keep their ordering and structure, but the content of every
///         <see cref="ToolResultContent"/> block is replaced with a placeholder and the message state is marked as
///         <see cref="CompactionState.Masked"/>.
///     </para>
///     <para>
///         The boundary moves in steps and holds between them, so the result of one call is the start of the result
///         of the next call until the history has grown by about a step. The boundary is derived from the history
///         and the budget alone: the strategy keeps no state between calls, and the same messages always give the
///         same result.
///     </para>
/// </remarks>
internal sealed class SlidingWindowStrategy : ICompactionStrategy
{
    private readonly ITokenCounter _tokenCounter;
    private readonly SlidingWindowOptions _options;
    private readonly ILogger _logger;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SlidingWindowStrategy"/> class with default options.
    /// </summary>
    /// <remarks>
    ///     This constructor uses <see cref="SlidingWindowOptions.Default"/> so callers can adopt the standard
    ///     sliding-window behavior without explicitly creating an options value. The supplied
    ///     <paramref name="tokenCounter"/> is retained and used for every later <c>CompactAsync</c> call.
    /// </remarks>
    /// <param name="tokenCounter">The token counter used to measure message cost during compaction.</param>
    public SlidingWindowStrategy(ITokenCounter tokenCounter)
        : this(tokenCounter, SlidingWindowOptions.Default)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="SlidingWindowStrategy"/> class.
    /// </summary>
    /// <remarks>
    ///     Supply <paramref name="options"/> to tune how much of the newest history stays untouched and how older
    ///     tool results are represented after masking. The provided value is retained for all subsequent
    ///     <c>CompactAsync</c> calls.
    /// </remarks>
    /// <param name="tokenCounter">The token counter used to measure candidate and compacted messages.</param>
    /// <param name="options">The sliding-window configuration that controls boundary selection and placeholder generation.</param>
    public SlidingWindowStrategy(ITokenCounter tokenCounter, SlidingWindowOptions options)
        : this(tokenCounter, options, NullLogger<SlidingWindowStrategy>.Instance)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="SlidingWindowStrategy"/> class that logs its masking decisions.
    /// </summary>
    /// <param name="tokenCounter">The token counter used to measure candidate and compacted messages.</param>
    /// <param name="options">The sliding-window configuration that controls boundary selection and placeholder generation.</param>
    /// <param name="logger">The logger that receives one record per pass and one per masked tool result.</param>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="tokenCounter"/> or <paramref name="logger"/> is <see langword="null"/>.
    /// </exception>
    public SlidingWindowStrategy(ITokenCounter tokenCounter, SlidingWindowOptions options, ILogger<SlidingWindowStrategy> logger)
    {
        ArgumentNullException.ThrowIfNull(tokenCounter);
        ArgumentNullException.ThrowIfNull(logger);

        this._tokenCounter = tokenCounter;
        this._options = options;
        this._logger = logger;
    }

    /// <summary>
    ///     Compacts a message history by masking older tool results outside the protected window.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This method preserves the original ordering of <paramref name="messages"/> while calculating a protected
    ///         tail from the newest compactable message backward. The protected tail always includes at least
    ///         <see cref="SlidingWindowOptions.WindowSize"/> newest messages when available. Masking starts once the
    ///         history exceeds <see cref="SlidingWindowOptions.ProtectedWindowFraction"/> of
    ///         <paramref name="availableTokens"/>; the tail then holds between that bound and one step below it, and its
    ///         start moves one step at a time as the history grows.
    ///     </para>
    ///     <para>
    ///         Callers are expected to exclude pinned messages before invoking this method. As a result, every entry in
    ///         <paramref name="messages"/> is treated as an eligible compaction candidate, and token calculations assume
    ///         <paramref name="availableTokens"/> already reflects any pinned-message cost deducted by the caller.
    ///     </para>
    ///     <para>
    ///         Messages before the protected boundary are only changed when they contain <see cref="ToolResultContent"/>.
    ///         Each such block is replaced with a <see cref="TextContent"/> placeholder built from
    ///         <see cref="SlidingWindowOptions.PlaceholderFormat"/>, and the returned message clears
    ///         <see cref="ContextMessage.TokenCount"/> so token estimation can be recomputed against the masked content.
    ///     </para>
    /// </remarks>
    /// <param name="messages">
    ///     The ordered compactable message history to process. Pinned messages must already be excluded, so all entries
    ///     are eligible for boundary evaluation and masking.
    /// </param>
    /// <param name="availableTokens">
    ///     The number of tokens available for the protected window. Callers deduct pinned-message cost from the total
    ///     context budget before passing this value.
    /// </param>
    /// <param name="cancellationToken">A token that can cancel the compaction operation before it completes.</param>
    /// <returns>
    ///     A task that resolves to a <see cref="CompactionResult"/> whose <see cref="CompactionResult.Messages"/>
    ///     value preserves the original message sequence when the entire compactable history fits inside the protected
    ///     window, including when <paramref name="messages"/> is empty; otherwise, older tool results are replaced with
    ///     placeholders and the result reports the associated metrics.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="messages"/> is <see langword="null"/>.</exception>
    public Task<CompactionResult> CompactAsync(
        IReadOnlyList<ContextMessage> messages,
        int availableTokens,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        cancellationToken.ThrowIfCancellationRequested();

        var tokenCounts = new int[messages.Count];

        for (var i = 0; i < messages.Count; i++)
        {
            tokenCounts[i] = messages[i].TokenCount ?? this._tokenCounter.Count(messages[i]);
        }

        var tokensBefore = tokenCounts.Sum();
        var highMark = (int)Math.Floor(availableTokens * this._options.ProtectedWindowFraction);
        var highBoundary = FindProtectedStart(tokenCounts, this._options.WindowSize, highMark);

        if (highBoundary == 0)
        {
            SlidingWindowLog.SlidingWindowApplied(
                this._logger, messages.Count, availableTokens, tokensBefore, tokensBefore, this._options.WindowSize, messages.Count, 0);

            return Task.FromResult(new CompactionResult(
                messages,
                tokensBefore,
                tokensBefore,
                0,
                nameof(SlidingWindowStrategy)));
        }

        var boundary = FindSteppedBoundary(tokenCounts, this._options.WindowSize, highMark, highBoundary);
        var protectedCount = messages.Count - boundary;
        var result = new ContextMessage[messages.Count];
        var messagesAffected = 0;
        var toolResultsMasked = 0;
        var logMasking = this._logger.IsEnabled(LogLevel.Debug);

        for (var i = 0; i < boundary; i++)
        {
            result[i] = MaskToolResultsIfNeeded(messages[i], this._options.PlaceholderFormat);

            if (messages[i].State == CompactionState.Original && result[i].State == CompactionState.Masked)
            {
                messagesAffected++;
            }

            if (logMasking && !ReferenceEquals(result[i], messages[i]))
            {
                toolResultsMasked += this.LogMaskedToolResults(i, messages[i], result[i]);
            }
        }

        for (var i = boundary; i < messages.Count; i++)
        {
            result[i] = messages[i];
        }

        var tokensAfter = CountTokens(result, this._tokenCounter);

        SlidingWindowLog.SlidingWindowApplied(
            this._logger, messages.Count, availableTokens, tokensBefore, tokensAfter, this._options.WindowSize, protectedCount, toolResultsMasked);

        return Task.FromResult(new CompactionResult(
            result,
            tokensBefore,
            tokensAfter,
            messagesAffected,
            nameof(SlidingWindowStrategy)));
    }

    /// <summary>
    ///     Writes one trace record for each tool result masked in a message and counts them.
    /// </summary>
    /// <remarks>
    ///     Call this only when debug logging is enabled. The per-message token counts are computed only when trace
    ///     logging is enabled.
    /// </remarks>
    /// <param name="messageIndex">The index of the message in the compactable history.</param>
    /// <param name="original">The message before masking.</param>
    /// <param name="masked">The message after masking.</param>
    /// <returns>The number of tool results masked in the message.</returns>
    private int LogMaskedToolResults(int messageIndex, ContextMessage original, ContextMessage masked)
    {
        var traceEnabled = this._logger.IsEnabled(LogLevel.Trace);
        var tokensBefore = traceEnabled ? original.TokenCount ?? this._tokenCounter.Count(original) : 0;
        var tokensAfter = traceEnabled ? this._tokenCounter.Count(masked) : 0;
        var toolResults = 0;

        foreach (var segment in masked.Segments)
        {
            if (segment is not ToolResultContent toolResult)
                continue;

            toolResults++;
            SlidingWindowLog.ToolResultMasked(this._logger, messageIndex, toolResult.ToolCallId, toolResult.ToolName, tokensBefore, tokensAfter);
        }

        return toolResults;
    }

    private static int CountTokens(IReadOnlyList<ContextMessage> messages, ITokenCounter tokenCounter)
    {
        return messages.Sum(message => message.TokenCount ?? tokenCounter.Count(message));
    }

    /// <summary>
    ///     Finds the index of the oldest message that a backward walk from the newest message protects.
    /// </summary>
    /// <remarks>
    ///     The walk protects the newest <paramref name="windowSize"/> messages whatever they cost, and then keeps
    ///     going while the protected tokens stay within <paramref name="allowance"/>.
    /// </remarks>
    /// <param name="tokenCounts">The token count of each message, oldest first.</param>
    /// <param name="windowSize">The number of newest messages that are always protected.</param>
    /// <param name="allowance">The most tokens the protected messages may hold once the floor is met.</param>
    /// <returns>The index of the first protected message, or the message count when there are no messages.</returns>
    private static int FindProtectedStart(int[] tokenCounts, int windowSize, int allowance)
    {
        var protectedTokens = 0;
        var start = tokenCounts.Length;

        for (var i = tokenCounts.Length - 1; i >= 0; i--)
        {
            if (tokenCounts.Length - start >= windowSize && protectedTokens + tokenCounts[i] > allowance)
            {
                break;
            }

            protectedTokens += tokenCounts[i];
            start = i;
        }

        return start;
    }

    /// <summary>
    ///     Finds the masking boundary, which moves in steps so that consecutive results share their start.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The step is <see cref="SlidingWindowDefaults.MaskingStepFraction"/> of <paramref name="highMark"/>, and
    ///         the low mark is the high mark minus one step. Step lines are fixed positions in the history: index 0, and
    ///         the first message boundary at or after each multiple of the step, counted in tokens from the oldest
    ///         message. The boundary is the last step line at or before the index the walk reaches with the low mark as
    ///         its allowance.
    ///     </para>
    ///     <para>
    ///         The boundary depends only on the token counts and the marks, so the same history always gives the same
    ///         boundary and it stays in place while the history grows by less than a step. It is never before
    ///         <paramref name="highBoundary"/>, which keeps a result from growing past the high mark. A step of zero
    ///         tokens gives <paramref name="highBoundary"/>.
    ///     </para>
    /// </remarks>
    /// <param name="tokenCounts">The token count of each message, oldest first.</param>
    /// <param name="windowSize">The number of newest messages that are always protected.</param>
    /// <param name="highMark">The most tokens the protected messages may hold once the floor is met.</param>
    /// <param name="highBoundary">The index the walk reaches with <paramref name="highMark"/> as its allowance.</param>
    /// <returns>The index of the first message that is returned unchanged.</returns>
    private static int FindSteppedBoundary(int[] tokenCounts, int windowSize, int highMark, int highBoundary)
    {
        var stepTokens = (int)Math.Floor(highMark * SlidingWindowDefaults.MaskingStepFraction);

        if (stepTokens <= 0)
        {
            return highBoundary;
        }

        var lowBoundary = FindProtectedStart(tokenCounts, windowSize, highMark - stepTokens);
        var lastStepLine = 0;
        var tokensBeforeIndex = 0;

        for (var i = 1; i <= lowBoundary; i++)
        {
            var stepsBefore = tokensBeforeIndex / stepTokens;
            tokensBeforeIndex += tokenCounts[i - 1];

            if (tokensBeforeIndex / stepTokens > stepsBefore)
            {
                lastStepLine = i;
            }
        }

        return Math.Max(lastStepLine, highBoundary);
    }

    /// <summary>
    ///     Replaces the content of every tool result in a message with a placeholder.
    /// </summary>
    /// <remarks>
    ///     The placeholder names the tool recorded on the result itself, so each masked result keeps its own
    ///     <see cref="ToolResultContent.ToolName"/> and <see cref="ToolResultContent.ToolCallId"/> even when another
    ///     tool call in the history has the same identifier.
    /// </remarks>
    /// <param name="contextMessage">The message to mask.</param>
    /// <param name="placeholderFormat">The format that receives the tool name as <c>{0}</c> and the tool call identifier as <c>{1}</c>.</param>
    /// <returns>The same message when it holds no tool result; otherwise, a masked copy.</returns>
    private static ContextMessage MaskToolResultsIfNeeded(ContextMessage contextMessage, string placeholderFormat)
    {
        var content = contextMessage.Segments;
        var hasToolResult = false;

        foreach (var contentSegment in content)
        {
            if (contentSegment is not ToolResultContent) continue;

            hasToolResult = true;
            break;
        }

        if (!hasToolResult)
        {
            return contextMessage;
        }

        var replacedContent = new ContentSegment[content.Count];

        for (var i = 0; i < content.Count; i++)
        {
            if (content[i] is ToolResultContent toolResult)
            {
                replacedContent[i] = toolResult with { Content = string.Format(placeholderFormat, toolResult.ToolName, toolResult.ToolCallId) };
                continue;
            }

            replacedContent[i] = content[i];
        }

        return contextMessage with
        {
            Segments = replacedContent,
            State = CompactionState.Masked,
            TokenCount = null,
        };
    }
}
