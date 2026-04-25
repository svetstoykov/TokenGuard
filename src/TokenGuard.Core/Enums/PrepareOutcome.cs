namespace TokenGuard.Core.Enums;

/// <summary>
/// Describes the state of the context after a <see cref="Abstractions.IConversationContext.PrepareAsync"/> call.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Ready"/> means the context is within budget and no compaction ran.
/// <see cref="Compacted"/> means compaction ran and the result fits within the budget.
/// <see cref="Degraded"/> means compaction and emergency truncation ran but the result still exceeds the budget.
/// <see cref="ContextExhausted"/> means the context contains irreducible content that alone exceeds the budget.
/// </para>
/// </remarks>
public enum PrepareOutcome
{
    /// <summary>
    /// The context is within budget and no compaction was required.
    /// </summary>
    Ready,

    /// <summary>
    /// Compaction ran successfully and the resulting token total is at or below the budget.
    /// </summary>
    Compacted,

    /// <summary>
    /// Compaction and emergency truncation ran but the token total still exceeds the budget.
    /// The agent may attempt the call but it will likely be rejected by the provider.
    /// </summary>
    Degraded,

    /// <summary>
    /// The context contains structural content (pinned messages, system prompt, or a single message)
    /// that alone exceeds the full budget, making compaction impossible.
    /// The agent should not attempt the call.
    /// </summary>
    ContextExhausted,
}
