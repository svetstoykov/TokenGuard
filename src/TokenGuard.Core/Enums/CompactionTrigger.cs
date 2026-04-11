using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Contexts;
using TokenGuard.Core.Models;

namespace TokenGuard.Core.Enums;

/// <summary>
/// Identifies which threshold caused a compaction cycle to begin.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ConversationContext"/> operates a two-tier threshold system defined by
/// <see cref="Models.ContextBudget"/>. The first tier fires the configured
/// <see cref="ICompactionStrategy"/> to reduce the history to a manageable size.
/// The second tier activates when the strategy alone was insufficient, and oldest-first
/// emergency truncation runs on top of the strategy result.
/// </para>
/// <para>
/// This value is surfaced through <see cref="Models.CompactionEvent.Trigger"/> so observers
/// can distinguish routine compaction from emergency intervention without inspecting token counts
/// directly.
/// </para>
/// </remarks>
public enum CompactionTrigger
{
    /// <summary>
    /// Compaction was triggered because the estimated token total reached
    /// <see cref="ContextBudget.CompactionTriggerTokens"/>.
    /// </summary>
    /// <remarks>
    /// The configured <see cref="ICompactionStrategy"/> ran and its result fit within
    /// the budget. No emergency truncation was applied.
    /// </remarks>
    Normal,

    /// <summary>
    /// Compaction was triggered because the estimated token total reached
    /// <see cref="ContextBudget.EmergencyTriggerTokens"/>.
    /// </summary>
    /// <remarks>
    /// This value is set when the history exceeded the emergency threshold before or after the
    /// primary compaction strategy ran. Oldest-first message truncation was applied on top of the
    /// strategy result to bring the payload back within budget.
    /// </remarks>
    Emergency,
}
