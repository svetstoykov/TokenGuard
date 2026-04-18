using TokenGuard.Core.Models;

namespace TokenGuard.Core.Contexts;

/// <summary>
/// Represents one reasoning turn in a conversation — a model response together with any tool results that follow it.
/// </summary>
/// <remarks>
/// <para>
/// A turn groups the messages that belong to a single model-initiated reasoning step. It starts with a
/// <see cref="Enums.MessageRole.Model"/> response and collects all subsequent
/// <see cref="Enums.MessageRole.Tool"/> results until the next model response or user input arrives.
/// </para>
/// <para>
/// <see cref="Enums.MessageRole.User"/> and <see cref="Enums.MessageRole.System"/> messages always occupy
/// their own standalone turns. A user message that follows tool results starts a new turn rather than
/// continuing the model-initiated one. This rule trades expressiveness for consistency: every
/// <c>User</c> boundary is unambiguous regardless of what preceded it, so compaction can identify
/// droppable units without inspecting content.
/// </para>
/// <para>
/// <see cref="TokenTotal"/> is <see langword="null"/> until the turn has been counted as a unit. Any
/// mutation via <see cref="Append"/> or <see cref="ReplaceOnly"/> resets it to <see langword="null"/>
/// so stale cached totals cannot be reused by later compaction passes.
/// </para>
/// <para>
/// This type is intentionally internal. It is a structural aid for compaction write-back, not a
/// public contract. External callers observe history only through the flat
/// <see cref="ConversationContext.History"/> property.
/// </para>
/// </remarks>
internal sealed class AgentTurn
{
    private readonly List<ContextMessage> _messages;

    /// <summary>
    /// Initializes a new turn starting with the supplied message.
    /// </summary>
    /// <param name="first">The first (and initially only) message in the turn.</param>
    internal AgentTurn(ContextMessage first)
    {
        this._messages = [first];
    }

    /// <summary>
    /// Gets the ordered messages that make up this turn.
    /// </summary>
    internal IReadOnlyList<ContextMessage> Messages => this._messages;

    /// <summary>
    /// Gets or sets the cached total token cost of all messages in this turn.
    /// </summary>
    /// <remarks>
    /// Set to <see langword="null"/> on any structural mutation so callers know the value is stale
    /// and must be recomputed before being used in compaction budget calculations.
    /// </remarks>
    internal int? TokenTotal { get; set; }

    /// <summary>
    /// Gets a value indicating whether any message in this turn is pinned.
    /// </summary>
    /// <remarks>
    /// A turn with at least one pinned message is excluded from the compactable set and must be
    /// preserved at its recorded position through any compaction pass.
    /// </remarks>
    internal bool HasPinnedMessages => this._messages.Any(m => m.IsPinned);

    /// <summary>
    /// Appends a message to this turn and invalidates the cached token total.
    /// </summary>
    /// <param name="message">The message to append.</param>
    internal void Append(ContextMessage message)
    {
        this._messages.Add(message);
        this.TokenTotal = null;
    }

    /// <summary>
    /// Replaces the sole message in this turn and invalidates the cached token total.
    /// </summary>
    /// <param name="message">The replacement message.</param>
    /// <remarks>
    /// Only valid for single-message turns. Used exclusively by
    /// <see cref="ConversationContext.SetSystemPrompt"/> to update the system prompt in place
    /// without inserting a new turn.
    /// </remarks>
    internal void ReplaceOnly(ContextMessage message)
    {
        this._messages[0] = message;
        this.TokenTotal = null;
    }
}
