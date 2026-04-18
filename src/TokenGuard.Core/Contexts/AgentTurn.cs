using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;

namespace TokenGuard.Core.Contexts;

/// <summary>
/// Represents one reasoning turn within a conversation.
/// </summary>
/// <remarks>
/// <para>
/// An <see cref="AgentTurn"/> groups messages that belong to a single unit of agent reasoning.
/// A turn begins when a <see cref="MessageRole.Model"/> message is recorded and absorbs any
/// subsequent <see cref="MessageRole.Tool"/> messages until the next model message arrives.
/// </para>
/// <para>
/// <strong>User message boundary rule:</strong> a <see cref="MessageRole.User"/> message always
/// starts its own turn. It is never merged into a preceding model-initiated turn, even when tool
/// results from that turn are still pending. This keeps user input visually and structurally
/// distinct in the history, which simplifies compaction logic that drops or masks entire turns.
/// System messages and pinned messages of any role also receive their own single-message turns.
/// </para>
/// <para>
/// Pinned messages are stored inside <see cref="AgentTurn"/> wrappers with <see cref="HasPinnedMessages"/>
/// set so the flattening path and compaction logic can identify them. Pinned turns are excluded from
/// compaction and emergency truncation.
/// </para>
/// </remarks>
internal sealed record AgentTurn
{
    private readonly List<ContextMessage> _messages = [];

    /// <summary>
    /// Gets the ordered messages that belong to this turn.
    /// </summary>
    public List<ContextMessage> Messages => this._messages;

    /// <summary>
    /// Gets the total token count for all messages in this turn, or <see langword="null"/> when
    /// the total has not yet been computed.
    /// </summary>
    public int? TokenCount { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether any message in this turn is pinned.
    /// </summary>
    public bool HasPinnedMessages { get; internal set; }

    /// <summary>
    /// Creates an empty turn ready to receive messages.
    /// </summary>
    public AgentTurn()
    {
    }

    /// <summary>
    /// Appends a message to this turn and updates the pinned flag if needed.
    /// </summary>
    /// <param name="message">The message to add.</param>
    public void AddMessage(ContextMessage message)
    {
        this._messages.Add(message);
        if (message.IsPinned)
        {
            this.HasPinnedMessages = true;
        }
    }
}
