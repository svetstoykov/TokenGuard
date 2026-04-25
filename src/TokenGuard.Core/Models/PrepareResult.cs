using TokenGuard.Core.Enums;

namespace TokenGuard.Core.Models;

/// <summary>
/// Carries the prepared message list and metadata describing what happened during context preparation.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="PrepareResult"/> is returned by <see cref="Abstractions.IConversationContext.PrepareAsync"/>.
/// It provides the caller with the messages to send to the provider alongside structured information
/// about whether compaction occurred, how many tokens were affected, and whether the context is in a
/// healthy state for sending.
/// </para>
/// <para>
/// Use <see cref="Outcome"/> to decide whether to proceed with the LLM call.
/// <see cref="PrepareOutcome.Ready"/> and <see cref="PrepareOutcome.Compacted"/> indicate a healthy context.
/// <see cref="PrepareOutcome.Degraded"/> means the agent may attempt the call but it will likely be rejected.
/// <see cref="PrepareOutcome.ContextExhausted"/> means the call should not be attempted.
/// </para>
/// </remarks>
public sealed record PrepareResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PrepareResult"/> record.
    /// </summary>
    /// <param name="messages">The prepared message list to send to the provider.</param>
    /// <param name="outcome">The outcome describing what happened during preparation.</param>
    /// <param name="tokensBeforeCompaction">The token total before any compaction ran.</param>
    /// <param name="tokensAfterCompaction">The token total of <paramref name="messages"/> after all compaction and truncation.</param>
    /// <param name="messagesCompacted">The count of messages removed or replaced during this call.</param>
    /// <param name="degradationReason">A descriptive reason when the outcome is degraded or exhausted; null otherwise.</param>
    public PrepareResult(
        IReadOnlyList<ContextMessage> messages,
        PrepareOutcome outcome,
        int tokensBeforeCompaction,
        int tokensAfterCompaction,
        int messagesCompacted,
        string? degradationReason = null)
    {
        this.Messages = messages ?? throw new ArgumentNullException(nameof(messages));
        this.Outcome = outcome;
        this.TokensBeforeCompaction = tokensBeforeCompaction;
        this.TokensAfterCompaction = tokensAfterCompaction;
        this.MessagesCompacted = messagesCompacted;
        this.DegradationReason = degradationReason;
    }

    /// <summary>
    /// Gets the prepared message list to send to the provider.
    /// </summary>
    public IReadOnlyList<ContextMessage> Messages { get; }

    /// <summary>
    /// Gets the outcome describing what happened during preparation.
    /// </summary>
    public PrepareOutcome Outcome { get; }

    /// <summary>
    /// Gets the token total when <see cref="Abstractions.IConversationContext.PrepareAsync"/> was called, before any strategy ran.
    /// Equal to <see cref="TokensAfterCompaction"/> when <see cref="Outcome"/> is <see cref="PrepareOutcome.Ready"/>.
    /// </summary>
    public int TokensBeforeCompaction { get; }

    /// <summary>
    /// Gets the token total of <see cref="Messages"/> after all compaction and truncation.
    /// </summary>
    public int TokensAfterCompaction { get; }

    /// <summary>
    /// Gets the count of messages removed or replaced during this call.
    /// Zero when <see cref="Outcome"/> is <see cref="PrepareOutcome.Ready"/>.
    /// </summary>
    public int MessagesCompacted { get; }

    /// <summary>
    /// Gets a descriptive reason when <see cref="Outcome"/> is <see cref="PrepareOutcome.Degraded"/> or
    /// <see cref="PrepareOutcome.ContextExhausted"/>; null otherwise.
    /// </summary>
    public string? DegradationReason { get; }
}
