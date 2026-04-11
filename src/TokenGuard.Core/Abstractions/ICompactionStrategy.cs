using TokenGuard.Core.Models;

namespace TokenGuard.Core.Abstractions;

/// <summary>
///     Defines a synchronous strategy for compacting message history to fit within a context budget.
/// </summary>
/// <remarks>
///     Implement this interface when a conversation history must be transformed before it is sent to a model,
///     but the caller needs to preserve a simple synchronous execution flow. Implementations are responsible for
///     deciding which parts of the message sequence are retained, summarized, or replaced while maintaining a
///     valid <see cref="ContextMessage"/> list for downstream processing.
/// </remarks>
public interface ICompactionStrategy
{
    /// <summary>
    ///     Compacts messages to fit within <paramref name="budget"/>'s available tokens.
    /// </summary>
    /// <remarks>
    ///     Implementations should preserve the logical ordering of <paramref name="messages"/> while producing a
    ///     sequence suitable for the token constraints described by <paramref name="budget"/>. The supplied
    ///     <paramref name="tokenCounter"/> is the abstraction used to estimate message cost and should be used
    ///     consistently so compaction decisions align with the active counting strategy. The task-based contract
    ///     allows implementations to call external services, including LLM-backed summarizers or reducers,
    ///     without forcing callers to block a thread.
    /// </remarks>
    /// <param name="messages">
    ///     The ordered source message list to compact. Callers must exclude pinned messages before invoking this
    ///     method, so every entry in the supplied sequence is eligible for compaction or replacement by the
    ///     implementation.
    /// </param>
    /// <param name="budget">The context budget that defines the available-token limits for the compacted result.</param>
    /// <param name="tokenCounter">The token counter used to measure message cost during compaction.</param>
    /// <returns>
    ///     A task that resolves to a <see cref="CompactionResult"/> containing the compacted messages together with
    ///     metrics describing what the strategy changed and how many tokens the result consumes.
    /// </returns>
    Task<CompactionResult> CompactAsync(IReadOnlyList<ContextMessage> messages, ContextBudget budget, ITokenCounter tokenCounter, CancellationToken cancellationToken = default);
}
