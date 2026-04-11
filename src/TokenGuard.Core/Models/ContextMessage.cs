using TokenGuard.Core.Contexts;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models.Content;

namespace TokenGuard.Core.Models;

/// <summary>
/// Represents one immutable unit of  conversation history.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ContextMessage"/> is the canonical unit recorded by <see cref="ConversationContext"/> and consumed by
/// compaction strategies, token counters, and provider adapters. Each instance pairs a <see cref="MessageRole"/> with
/// one or more <see cref="ContentSegment"/> values so a single turn can carry plain text, tool-use instructions, or tool
/// results.
/// </para>
/// <para>
/// The record is immutable to external callers. Services may populate <see cref="TokenCount"/> internally as an
/// optimization so repeated preparation and compaction passes do not need to recount unchanged messages.
/// </para>
/// </remarks>
public sealed record ContextMessage
{
    private readonly IReadOnlyList<ContentSegment> _content = [];

    /// <summary>
    /// Gets the participant role that produced this message.
    /// </summary>
    public required MessageRole Role { get; init; }

    /// <summary>
    /// Gets the ordered content segments carried by this message.
    /// </summary>
    /// <remarks>
    /// A message must contain at least one segment. The assigned sequence is defensively copied during initialization so
    /// later external mutations do not affect recorded conversation history.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when the assigned value is empty.</exception>
    /// <exception cref="ArgumentNullException">Thrown when the assigned value is null.</exception>
    public required IReadOnlyList<ContentSegment> Content
    {
        get => this._content;
        init
        {
            ArgumentNullException.ThrowIfNull(value);

            if (value.Count == 0)
            {
                throw new ArgumentException("Content must contain at least one segment.", nameof(this.Content));
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
    /// TokenGuard sets this property internally after the first successful count so later operations can reuse the
    /// result. External consumers should treat <see cref="TokenCount"/> as observational metadata rather than part of
    /// the logical message payload.
    /// </remarks>
    public int? TokenCount { get; internal set; }

    /// <summary>
    /// Creates a <see cref="ContextMessage"/> containing a single <see cref="TextContent"/> segment.
    /// </summary>
    /// <remarks>
    /// Use this helper for plain-text system, user, or model turns when no richer segment structure is needed.
    /// </remarks>
    /// <param name="role">The participant role that produced the message.</param>
    /// <param name="text">The text payload to wrap in a <see cref="TextContent"/> segment.</param>
    /// <returns>A new <see cref="ContextMessage"/> containing exactly one <see cref="TextContent"/> segment.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="text"/> is null or whitespace.</exception>
    public static ContextMessage FromText(MessageRole role, string text)
    {
        return new ContextMessage
        {
            Role = role,
            Content = [new TextContent(text)],
        };
    }

    /// <summary>
    /// Creates a <see cref="ContextMessage"/> from a single existing <see cref="ContentSegment"/>.
    /// </summary>
    /// <remarks>
    /// This overload is useful when a caller has already created a specific segment type, such as
    /// <see cref="ToolUseContent"/> or <see cref="ToolResultContent"/>.
    /// </remarks>
    /// <param name="role">The participant role that produced the message.</param>
    /// <param name="segment">The content segment to store as the sole payload of the message.</param>
    /// <returns>A new <see cref="ContextMessage"/> containing exactly one content segment.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="segment"/> is null.</exception>
    public static ContextMessage FromContent(MessageRole role, ContentSegment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);

        return new ContextMessage
        {
            Role = role,
            Content = [segment],
        };
    }

    /// <summary>
    /// Creates a <see cref="ContextMessage"/> from multiple existing <see cref="ContentSegment"/> values.
    /// </summary>
    /// <remarks>
    /// Use this overload when a single turn must preserve multiple content segments in order, such as a mixed text and
    /// tool-call model response.
    /// </remarks>
    /// <param name="role">The participant role that produced the message.</param>
    /// <param name="blocks">The ordered content segments that make up the message payload.</param>
    /// <returns>A new <see cref="ContextMessage"/> containing the supplied content segments.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="blocks"/> is null.</exception>
    public static ContextMessage FromContent(MessageRole role, ContentSegment[] blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        return new ContextMessage
        {
            Role = role,
            Content = blocks,
        };
    }
}
