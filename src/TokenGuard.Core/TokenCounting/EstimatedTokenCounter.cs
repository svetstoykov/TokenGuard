using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;

namespace TokenGuard.Core.TokenCounting;

/// <summary>
/// Provides a lightweight heuristic <see cref="ITokenCounter"/> based on character counts.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="EstimatedTokenCounter"/> is the default counter used by <see cref="ConversationConfigBuilder"/> when no
/// provider-specific implementation is supplied. It trades precision for zero dependencies and predictable performance.
/// </para>
/// <para>
/// The estimate is calculated as <c>ceiling(totalCharacters / 4.0) + 4</c> per message. This is intentionally simple,
/// and callers should replace it when exact provider tokenization materially affects compaction behavior.
/// </para>
/// <para>
/// Character counts are based on <see cref="string.Length"/>, which measures UTF-16 code units rather than Unicode
/// scalar values or grapheme clusters. That means supplementary-plane characters such as emoji can count as more than
/// one character for this heuristic, by design, in exchange for keeping the implementation allocation-free and fast.
/// </para>
/// </remarks>
public sealed class EstimatedTokenCounter : ITokenCounter
{
    private const int MessageOverhead = 4;
    private const double CharsPerToken = 4.0;

    /// <inheritdoc />
    public int Count(ContextMessage contextMessage)
    {
        ArgumentNullException.ThrowIfNull(contextMessage);

        if (contextMessage.TokenCount is > 0)
        {
            return contextMessage.TokenCount.Value;
        }

        long totalChars = 0;

        foreach (var segment in contextMessage.Content)
        {
            totalChars += segment switch
            {
                TextContent text => text.Content.Length,
                ToolUseContent toolUse => toolUse.ToolName.Length + toolUse.Content.Length,
                ToolResultContent toolResult => toolResult.ToolCallId.Length + toolResult.Content.Length,
                _ => 0
            };
        }

        return (int)Math.Ceiling(totalChars / CharsPerToken) + MessageOverhead;
    }

    /// <inheritdoc />
    public int Count(IEnumerable<ContextMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        return messages.Sum(this.Count);
    }
}
