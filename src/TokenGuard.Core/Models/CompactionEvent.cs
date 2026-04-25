using TokenGuard.Core.Enums;

namespace TokenGuard.Core.Models;

/// <summary>
/// Captures the outcome and context of a single compaction cycle.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="CompactionEvent"/> is produced by <c>ConversationContext.PrepareAsync</c> immediately after a
/// compaction cycle changes the prepared history. The cycle always includes a configured strategy evaluation and
/// can also include a later emergency truncation pass when the strategy output still exceeds the emergency threshold.
/// </para>
/// <para>
/// <see cref="Result"/> exposes aggregate cycle metrics plus explicit emergency dropped-message count so observers can
/// distinguish normal strategy effects from emergency message dropping without diffing message lists.
/// </para>
/// </remarks>
/// <param name="Result">The aggregate compaction diagnostics for this cycle, including any emergency dropped-message count.</param>
/// <param name="Timestamp">The point in time at which the compaction cycle completed.</param>
/// <param name="Trigger">Indicates whether the normal or emergency threshold caused this compaction cycle.</param>
/// <param name="BudgetAtTrigger">The token budget that was in effect when the threshold check fired.</param>
public sealed record CompactionEvent(
    CompactionResult Result,
    DateTimeOffset Timestamp,
    CompactionTrigger Trigger,
    ContextBudget BudgetAtTrigger);
