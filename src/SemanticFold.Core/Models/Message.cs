using SemanticFold.Core.Enums;
using SemanticFold.Core.Models.Content;

namespace SemanticFold.Core.Models;

/// <summary>
/// The immutable unit of conversation history.
/// </summary>
public sealed record Message
{
    private readonly IReadOnlyList<ContentBlock> _content = [];

    /// <summary>
    /// Gets the role that produced this message.
    /// </summary>
    public required MessageRole Role { get; init; }

    /// <summary>
    /// Gets the content blocks for this message.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when the assigned value is empty.</exception>
    /// <exception cref="ArgumentNullException">Thrown when the assigned value is null.</exception>
    public required IReadOnlyList<ContentBlock> Content
    {
        get => this._content;
        init
        {
            ArgumentNullException.ThrowIfNull(value);

            if (value.Count == 0)
            {
                throw new ArgumentException("Content must contain at least one block.", nameof(this.Content));
            }

            this._content = value.ToArray();
        }
    }

    /// <summary>
    /// Gets the compaction state for this message.
    /// </summary>
    public CompactionState State { get; init; } = CompactionState.Original;

    /// <summary>
    /// Gets the creation timestamp of this message.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets the token count for this message. Populated by the engine on first evaluation;
    /// <see langword="null"/> until counted. External consumers should treat this as read-only.
    /// </summary>
    public int? TokenCount { get; internal set; }

    /// <summary>
    /// Creates a single-block text message.
    /// </summary>
    /// <param name="role">The role that produced the message.</param>
    /// <param name="text">The message text.</param>
    /// <returns>A new <see cref="Message"/> containing one <see cref="TextContent"/> block.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="text"/> is null or whitespace.</exception>
    public static Message FromText(MessageRole role, string text)
    {
        return new Message
        {
            Role = role,
            Content = [new TextContent(text)],
        };
    }

    /// <summary>
    /// Creates a single-block message from an existing content block.
    /// </summary>
    /// <param name="role">The role that produced the message.</param>
    /// <param name="block">The content block to include.</param>
    /// <returns>A new <see cref="Message"/> containing one content block.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="block"/> is null.</exception>
    public static Message FromContent(MessageRole role, ContentBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);

        return new Message
        {
            Role = role,
            Content = [block],
        };
    }

    /// <summary>
    /// Creates a message from multiple content blocks.
    /// </summary>
    /// <param name="role">The role that produced the message.</param>
    /// <param name="blocks">The content blocks to include.</param>
    /// <returns>A new <see cref="Message"/> containing the supplied content blocks.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="blocks"/> is null.</exception>
    public static Message FromContent(MessageRole role, ContentBlock[] blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        return new Message
        {
            Role = role,
            Content = blocks,
        };
    }
}