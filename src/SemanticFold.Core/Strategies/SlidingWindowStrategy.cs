using SemanticFold.Core.Abstractions;
using SemanticFold.Core.Enums;
using SemanticFold.Core.Models;
using SemanticFold.Core.Models.Content;

namespace SemanticFold.Core.Strategies;

/// <summary>
/// Masks tool results in older messages while preserving a newest-message window unchanged.
/// </summary>
public sealed class SlidingWindowStrategy : ICompactionStrategy
{
    private readonly SlidingWindowOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="SlidingWindowStrategy"/> class with default options.
    /// </summary>
    public SlidingWindowStrategy()
        : this(SlidingWindowOptions.Default)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SlidingWindowStrategy"/> class.
    /// </summary>
    /// <param name="options">The sliding window options.</param>
    public SlidingWindowStrategy(SlidingWindowOptions options)
    {
        this._options = options;
    }

    /// <inheritdoc />
    public IReadOnlyList<Message> Compact(IReadOnlyList<Message> messages, ContextBudget budget, ITokenCounter tokenCounter)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(tokenCounter);

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

            var candidateTokens = tokenCounter.Count(messages[i]);
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
            return messages;
        }

        var toolNameLookup = BuildToolNameLookup(messages);
        var result = new Message[messages.Count];

        for (var i = 0; i < boundary; i++)
        {
            result[i] = MaskToolResultsIfNeeded(messages[i], toolNameLookup, this._options.PlaceholderFormat);
        }

        for (var i = boundary; i < messages.Count; i++)
        {
            result[i] = messages[i];
        }

        return result;
    }

    private static Dictionary<string, string> BuildToolNameLookup(IReadOnlyList<Message> messages)
    {
        var lookup = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var i = 0; i < messages.Count; i++)
        {
            var content = messages[i].Content;

            for (var j = 0; j < content.Count; j++)
            {
                if (content[j] is ToolUseContent toolUse)
                {
                    lookup[toolUse.ToolCallId] = toolUse.ToolName;
                }
            }
        }

        return lookup;
    }

    private static Message MaskToolResultsIfNeeded(
        Message message,
        IReadOnlyDictionary<string, string> toolNameLookup,
        string placeholderFormat)
    {
        var content = message.Content;
        var hasToolResult = false;

        for (var i = 0; i < content.Count; i++)
        {
            if (content[i] is ToolResultContent)
            {
                hasToolResult = true;
                break;
            }
        }

        if (!hasToolResult)
        {
            return message;
        }

        var replacedContent = new ContentBlock[content.Count];

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

        return message with
        {
            Content = replacedContent,
            State = CompactionState.Masked,
            TokenCount = null,
        };
    }
}
