using SemanticFold.Models.Messages;

namespace SemanticFold.Abstractions;

/// <summary>
/// Defines a synchronous strategy for compacting message history to fit within a context budget.
/// </summary>
public interface ICompactionStrategy
{
    /// <summary>
    /// Compacts messages to fit within <paramref name="budget"/>'s available tokens.
    /// </summary>
    /// <param name="messages">The source message list that should be compacted.</param>
    /// <param name="budget">The context budget that defines available token limits.</param>
    /// <param name="tokenCounter">The token counter used to measure token usage.</param>
    /// <returns>
    /// A new message list that preserves ordering and fits within <paramref name="budget"/>'s available tokens.
    /// </returns>
    IReadOnlyList<Message> Compact(IReadOnlyList<Message> messages, ContextBudget budget, ITokenCounter tokenCounter);
}
