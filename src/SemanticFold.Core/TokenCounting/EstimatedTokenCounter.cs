using SemanticFold.Core.Abstractions;
using SemanticFold.Core.Models;
using SemanticFold.Core.Models.Content;

namespace SemanticFold.Core.TokenCounting;

/// <summary>
/// Provides a lightweight heuristic <see cref="ITokenCounter"/> based on character counts.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="EstimatedTokenCounter"/> is the default counter used by <see cref="ConversationContextBuilder"/> when no
/// provider-specific implementation is supplied. It trades precision for zero dependencies and predictable performance.
/// </para>
/// <para>
/// The estimate is calculated as <c>ceiling(totalCharacters / 4.0) + 4</c> per message. This is intentionally simple,
/// and callers should replace it when exact provider tokenization materially affects compaction behavior.
/// </para>
/// </remarks>
public sealed class EstimatedTokenCounter : ITokenCounter
{
    private const int MessageOverhead = 4;
    private const double CharsPerToken = 4.0;

    /// <inheritdoc />
    public int Count(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.TokenCount is > 0)
        {
            return message.TokenCount.Value;
        }

        long totalChars = 0;

        foreach (var block in message.Content)
        {
            totalChars += block switch
            {
                TextContent text => text.Text.Length,
                ToolUseContent toolUse => toolUse.ToolName.Length + toolUse.ArgumentsJson.Length,
                ToolResultContent toolResult => toolResult.ToolCallId.Length + toolResult.Content.Length,
                _ => 0
            };
        }

        return (int)Math.Ceiling(totalChars / CharsPerToken) + MessageOverhead;
    }

    /// <inheritdoc />
    public int Count(IEnumerable<Message> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        return messages.Sum(this.Count);
    }
}
