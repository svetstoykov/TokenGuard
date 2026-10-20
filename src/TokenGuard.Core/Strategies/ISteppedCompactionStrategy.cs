using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Models;

namespace TokenGuard.Core.Strategies;

/// <summary>
///     Defines a built-in compaction strategy whose masking step lines are derived from a budget of their own.
/// </summary>
/// <remarks>
///     The conversation context converts the budget it passes to a strategy by the provider correction, so the marks of
///     the strategy follow the provider's count. The step lines of sliding-window masking stay on the budget before that
///     conversion, so a change in the correction moves where the low-mark walk ends but not the lines it snaps to.
/// </remarks>
internal interface ISteppedCompactionStrategy : ICompactionStrategy
{
    /// <summary>
    ///     Compacts messages to fit within <paramref name="availableTokens"/>, with step lines derived from
    ///     <paramref name="stepBudgetTokens"/>.
    /// </summary>
    /// <param name="messages">The ordered compactable message history, with pinned messages excluded.</param>
    /// <param name="availableTokens">
    ///     The budget available to the compacted result, converted by the provider correction, with the pinned estimate
    ///     subtracted.
    /// </param>
    /// <param name="stepBudgetTokens">
    ///     The budget the masking step lines are derived from: the maximum with the pinned estimate subtracted, not
    ///     converted by the provider correction.
    /// </param>
    /// <param name="cancellationToken">A token that cancels the compaction operation before it completes.</param>
    /// <returns>A task that resolves to the compacted messages and the metrics of the compaction.</returns>
    Task<CompactionResult> CompactAsync(
        IReadOnlyList<ContextMessage> messages, int availableTokens, int stepBudgetTokens, CancellationToken cancellationToken = default);
}
