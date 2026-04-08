using SemanticFold.Abstractions;
using SemanticFold.Models;
using SemanticFold.Models.Content;
using SemanticFold.Models.Messages;

namespace SemanticFold.TokenCounting;

/// <summary>
/// A token counter that estimates tokens based on character counts using a fixed ratio.
/// </summary>
/// <remarks>
/// Estimation formula: (int)Math.Ceiling(chars / 4.0).
/// Each message adds a fixed 4-token overhead.
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
