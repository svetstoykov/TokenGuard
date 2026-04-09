using SemanticFold.Core.Enums;
using SemanticFold.Core.Models.Content;

namespace SemanticFold.Core.Models;

/// <summary>
/// Represents one immutable unit of  conversation history.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Message"/> is the canonical unit recorded by <see cref="ConversationContext"/> and consumed by
/// compaction strategies, token counters, and provider adapters. Each instance pairs a <see cref="MessageRole"/> with
/// one or more <see cref="ContentBlock"/> values so a single turn can carry plain text, tool-use instructions, or tool
/// results.
/// </para>
/// <para>
/// The record is immutable to external callers. Services may populate <see cref="TokenCount"/> internally as an
/// optimization so repeated preparation and compaction passes do not need to recount unchanged messages.
/// </para>
/// </remarks>
public sealed record Message
{
    private readonly IReadOnlyList<ContentBlock> _content = [];

    /// <summary>
    /// Gets the participant role that produced this message.
    /// </summary>
    public required MessageRole Role { get; init; }

    /// <summary>
    /// Gets the ordered content blocks carried by this message.
    /// </summary>
    /// <remarks>
    /// A message must contain at least one block. The assigned sequence is defensively copied during initialization so
    /// later external mutations do not affect recorded conversation history.
    /// </remarks>
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
    /// Gets the compaction provenance for this message.
    /// </summary>
    public CompactionState State { get; init; } = CompactionState.Original;

    /// <summary>
    /// Gets the UTC timestamp captured when the message instance was created.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets the cached token count for this message.
    /// </summary>
    /// <remarks>
    /// SemanticFold sets this property internally after the first successful count so later operations can reuse the
    /// result. External consumers should treat <see cref="TokenCount"/> as observational metadata rather than part of
    /// the logical message payload.
    /// </remarks>
    public int? TokenCount { get; internal set; }

    /// <summary>
    /// Creates a <see cref="Message"/> containing a single <see cref="TextContent"/> block.
    /// </summary>
    /// <remarks>
    /// Use this helper for plain-text system, user, or model turns when no richer block structure is needed.
    /// </remarks>
    /// <param name="role">The participant role that produced the message.</param>
    /// <param name="text">The text payload to wrap in a <see cref="TextContent"/> block.</param>
    /// <returns>A new <see cref="Message"/> containing exactly one <see cref="TextContent"/> block.</returns>
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
    /// Creates a <see cref="Message"/> from a single existing <see cref="ContentBlock"/>.
    /// </summary>
    /// <remarks>
    /// This overload is useful when a caller has already created a specific block type, such as
    /// <see cref="ToolUseContent"/> or <see cref="ToolResultContent"/>.
    /// </remarks>
    /// <param name="role">The participant role that produced the message.</param>
    /// <param name="block">The content block to store as the sole payload of the message.</param>
    /// <returns>A new <see cref="Message"/> containing exactly one content block.</returns>
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
    /// Creates a <see cref="Message"/> from multiple existing <see cref="ContentBlock"/> values.
    /// </summary>
    /// <remarks>
    /// Use this overload when a single turn must preserve multiple content blocks in order, such as a mixed text and
    /// tool-call model response.
    /// </remarks>
    /// <param name="role">The participant role that produced the message.</param>
    /// <param name="blocks">The ordered content blocks that make up the message payload.</param>
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
