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
///         The strategy walks backward from the newest message and protects messages until either
///         <see cref="SlidingWindowOptions.WindowSize"/> is reached or the protected segment would exceed the
///         token allowance derived from <see cref="ContextBudget.AvailableTokens"/> and
///         <see cref="SlidingWindowOptions.ProtectedWindowFraction"/>. Messages before that boundary keep their
///         ordering and structure, but any <see cref="ToolResultContent"/> blocks are converted into text
///         placeholders and the message state is marked as <see cref="CompactionState.Masked"/>.
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
    ///     <see cref="CompactAsync(IReadOnlyList{ContextMessage}, ContextBudget, ITokenCounter, CancellationToken)"/> calls.
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
    ///         This method preserves the original message order and leaves the newest protected segment unchanged.
    ///         The protected boundary is calculated from the end of <paramref name="messages"/> using both the
    ///         configured window size and the token counts returned by <paramref name="tokenCounter"/>.
    ///     </para>
    ///     <para>
    ///         An empty <paramref name="messages"/> list is treated as a valid no-op input and is returned
    ///         unchanged so callers can compose compaction without adding special-case guards.
    ///     </para>
    ///     <para>
    ///         Messages before the boundary are only modified when they contain <see cref="ToolResultContent"/>.
    ///         In that case each tool result is replaced with a <see cref="TextContent"/> placeholder built from
    ///         <see cref="SlidingWindowOptions.PlaceholderFormat"/>, and the returned message clears
    ///         <see cref="ContextMessage.TokenCount"/> so token estimation can be recomputed against the masked content.
    ///     </para>
    /// </remarks>
    /// <param name="messages">The ordered message history to compact.</param>
    /// <param name="budget">The context budget that supplies the available-token limit for the protected window.</param>
    /// <param name="tokenCounter">The token counter used to measure candidate messages while determining the protected boundary.</param>
    /// <param name="cancellationToken">A token that can cancel the compaction operation before it completes.</param>
    /// <returns>
    ///     A task that resolves to a <see cref="CompactionResult"/> whose <see cref="CompactionResult.Messages"/>
    ///     value preserves the original message sequence when the entire history fits inside the protected window,
    ///     including when <paramref name="messages"/> is empty; otherwise, older tool results are replaced with
    ///     placeholders and the result reports the associated metrics.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="messages"/> or <paramref name="tokenCounter"/> is <see langword="null"/>.
    /// </exception>
    public Task<CompactionResult> CompactAsync(
        IReadOnlyList<ContextMessage> messages,
        ContextBudget budget,
        ITokenCounter tokenCounter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(tokenCounter);
        cancellationToken.ThrowIfCancellationRequested();

        var tokensBefore = CountTokens(messages, tokenCounter);

        var maxProtectedTokens = (int)Math.Floor(budget.AvailableTokens * this._options.ProtectedWindowFraction);
        var protectedCount = 0;
        var protectedTokens = 0;
        var boundary = messages.Count;

        for (var i = messages.Count - 1; i >= 0; i--)
        {
            if (protectedCount >= this._options.WindowSize)
            {
                break;
            }

            var candidateTokens = messages[i].TokenCount ?? tokenCounter.Count(messages[i]);
            if (protectedTokens + candidateTokens > maxProtectedTokens)
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
                false));
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
            messagesAffected > 0));
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
            var content = message.Content;

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
        var content = contextMessage.Content;
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

                replacedContent[i] = new TextContent(string.Format(placeholderFormat, resolvedName, toolResult.ToolCallId));
                continue;
            }

            replacedContent[i] = content[i];
        }

        return contextMessage with
        {
            Content = replacedContent,
            State = CompactionState.Masked,
            TokenCount = null,
        };
    }
}
