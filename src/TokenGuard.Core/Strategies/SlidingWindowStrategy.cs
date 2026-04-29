using TokenGuard.Core.Abstractions;
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
///         The strategy walks backward from the newest message in the compactable slice supplied by
///         <c>CompactAsync</c> and always protects at least <see cref="SlidingWindowOptions.WindowSize"/> messages when that many are
///         available. After that floor is satisfied, the protected segment continues growing while the token allowance
///         derived from <c>availableTokens</c> and
///         <see cref="SlidingWindowOptions.ProtectedWindowFraction"/> still permits more messages. Messages before that
///         boundary keep their ordering and structure, but any <see cref="ToolResultContent"/> blocks are converted into
///         text placeholders and the message state is marked as <see cref="CompactionState.Masked"/>.
///     </para>
/// </remarks>
internal sealed class SlidingWindowStrategy : ICompactionStrategy
{
    private readonly SlidingWindowOptions _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SlidingWindowStrategy"/> class with default options.
    /// </summary>
    /// <remarks>
    ///     This constructor uses <see cref="SlidingWindowOptions.Default"/> so callers can adopt the standard
    ///     sliding-window behavior without explicitly creating an options value.
    /// </remarks>
    public SlidingWindowStrategy()
        : this(SlidingWindowOptions.Default)
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
    /// <param name="options">The sliding-window configuration that controls boundary selection and placeholder generation.</param>
    public SlidingWindowStrategy(SlidingWindowOptions options)
    {
        this._options = options;
    }

    /// <summary>
    ///     Compacts a message history by masking older tool results outside the protected window.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This method preserves the original ordering of <paramref name="messages"/> while calculating a protected
    ///         tail from the newest compactable message backward. The protected boundary always includes at least
    ///         <see cref="SlidingWindowOptions.WindowSize"/> newest messages when available, and then expands further while
    ///         the token allowance produced from <paramref name="budget"/> and
    ///         <see cref="SlidingWindowOptions.ProtectedWindowFraction"/> is not exceeded.
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
    /// <param name="tokenCounter">The token counter used to measure candidate messages while determining the protected boundary.</param>
    /// <param name="cancellationToken">A token that can cancel the compaction operation before it completes.</param>
    /// <returns>
    ///     A task that resolves to a <see cref="CompactionResult"/> whose <see cref="CompactionResult.Messages"/>
    ///     value preserves the original message sequence when the entire compactable history fits inside the protected
    ///     window, including when <paramref name="messages"/> is empty; otherwise, older tool results are replaced with
    ///     placeholders and the result reports the associated metrics.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="messages"/> or <paramref name="tokenCounter"/> is <see langword="null"/>.
    /// </exception>
    public Task<CompactionResult> CompactAsync(
        IReadOnlyList<ContextMessage> messages,
        int availableTokens,
        ITokenCounter tokenCounter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(tokenCounter);
        cancellationToken.ThrowIfCancellationRequested();

        var tokensBefore = CountTokens(messages, tokenCounter);

        var maxProtectedTokens = (int)Math.Floor(availableTokens * this._options.ProtectedWindowFraction);
        var protectedCount = 0;
        var protectedTokens = 0;
        var boundary = messages.Count;

        for (var i = messages.Count - 1; i >= 0; i--)
        {
            var candidateTokens = messages[i].TokenCount ?? tokenCounter.Count(messages[i]);

            if (protectedCount >= this._options.WindowSize && protectedTokens + candidateTokens > maxProtectedTokens)
            {
                break;
            }

            protectedTokens += candidateTokens;
            protectedCount++;
            boundary = i;
        }

        if (protectedCount == messages.Count)
        {
            return Task.FromResult(new CompactionResult(
                messages,
                tokensBefore,
                tokensBefore,
                0,
                nameof(SlidingWindowStrategy),
                CompactionType.None));
        }

        var toolNameLookup = BuildToolNameLookup(messages);
        var result = new ContextMessage[messages.Count];
        var messagesAffected = 0;

        for (var i = 0; i < boundary; i++)
        {
            result[i] = MaskToolResultsIfNeeded(messages[i], toolNameLookup, this._options.PlaceholderFormat);

            if (messages[i].State == CompactionState.Original && result[i].State == CompactionState.Masked)
            {
                messagesAffected++;
            }
        }

        for (var i = boundary; i < messages.Count; i++)
        {
            result[i] = messages[i];
        }

        var tokensAfter = CountTokens(result, tokenCounter);

        return Task.FromResult(new CompactionResult(
            result,
            tokensBefore,
            tokensAfter,
            messagesAffected,
            nameof(SlidingWindowStrategy),
            messagesAffected > 0 ? CompactionType.Masking : CompactionType.None));
    }

    private static int CountTokens(IReadOnlyList<ContextMessage> messages, ITokenCounter tokenCounter)
    {
        return messages.Sum(message => message.TokenCount ?? tokenCounter.Count(message));
    }

    private static Dictionary<string, string> BuildToolNameLookup(IReadOnlyList<ContextMessage> messages)
    {
        var lookup = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var message in messages)
        {
            var content = message.Segments;

            foreach (var contentSegment in content)
            {
                if (contentSegment is ToolUseContent toolUse)
                {
                    lookup[toolUse.ToolCallId] = toolUse.ToolName;
                }
            }
        }

        return lookup;
    }

    private static ContextMessage MaskToolResultsIfNeeded(
        ContextMessage contextMessage,
        IReadOnlyDictionary<string, string> toolNameLookup,
        string placeholderFormat)
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
                var resolvedName = toolNameLookup.TryGetValue(toolResult.ToolCallId, out var toolName)
                    ? toolName
                    : toolResult.ToolCallId;

                replacedContent[i] = new ToolResultContent(
                    toolResult.ToolCallId,
                    resolvedName,
                    string.Format(placeholderFormat, resolvedName, toolResult.ToolCallId));
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
