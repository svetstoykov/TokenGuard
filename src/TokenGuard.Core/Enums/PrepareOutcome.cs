namespace TokenGuard.Core.Enums;

/// <summary>
/// Describes the state of the context after a <see cref="Abstractions.IConversationContext.PrepareAsync"/> call.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Ready"/> means the context is within budget and no compaction ran.
/// <see cref="Compacted"/> means compaction ran and the result fits within the budget.
/// <see cref="CompactionInsufficient"/> means compaction and emergency truncation ran but the result still exceeds the budget.
/// <see cref="CannotCompact"/> means the context contains irreducible content that alone exceeds the budget.
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
    /// <remarks>
    /// With emergency truncation enabled, the prepared messages are the ones it never drops: pinned messages, a summary
    /// message and everything after it, and the newest message. When the newest message is a tool result, they also
    /// include the model message that made the tool call, its other tool results, and the user message that opened that
    /// tool loop. With emergency truncation disabled, they are the messages the compaction strategy returned.
    /// </remarks>
    CompactionInsufficient,

    /// <summary>
    /// The context contains structural content (pinned messages, system prompt, or a single message)
    /// that alone exceeds the full budget, making compaction impossible.
    /// The agent should not attempt the call.
    /// </summary>
    /// <remarks>
    /// Nothing was masked, summarized, or dropped, so the prepared messages are the whole recorded history. A tool loop
    /// reports this outcome when the user message that opened it, the model message that made its first tool call, and
    /// the tool results of that call do not fit together.
    /// </remarks>
    CannotCompact,
}
