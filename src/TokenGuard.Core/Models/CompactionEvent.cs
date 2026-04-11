using TokenGuard.Core.Enums;

namespace TokenGuard.Core.Models;

/// <summary>
/// Captures the outcome and context of a single compaction cycle.
/// </summary>
/// <remarks>
/// A <see cref="CompactionEvent"/> is produced by <c>ConversationContext.PrepareAsync</c> immediately after a
/// compaction strategy runs and <see cref="CompactionResult.WasApplied"/> is <see langword="true"/>.
/// It wraps the <see cref="CompactionResult"/> returned by the strategy with additional context that the
/// strategy itself does not have access to: when the cycle occurred, which threshold caused it, and what the
/// budget looked like at the moment the trigger fired.
/// </remarks>
/// <param name="Result">The outcome reported by the compaction strategy for this cycle.</param>
/// <param name="Timestamp">The point in time at which the compaction cycle completed.</param>
/// <param name="Trigger">Indicates whether the normal or emergency threshold caused this compaction cycle.</param>
/// <param name="BudgetAtTrigger">The token budget that was in effect when the threshold check fired.</param>
public sealed record CompactionEvent(
    CompactionResult Result,
    DateTimeOffset Timestamp,
    CompactionTrigger Trigger,
    ContextBudget BudgetAtTrigger);
