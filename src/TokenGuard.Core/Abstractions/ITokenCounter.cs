using TokenGuard.Core.Models;

namespace TokenGuard.Core.Abstractions;

/// <summary>
/// Defines the token-counting abstraction used by TokenGuard when budgeting and compacting conversation history.
/// </summary>
/// <remarks>
/// <para>
/// Implement <see cref="ITokenCounter"/> when message cost must be estimated with provider-specific rules instead of
/// the library defaults. The same counter instance is used by <see cref="ConversationContext"/> while recording
/// messages and by <see cref="ICompactionStrategy"/> implementations while deciding which messages fit within a
/// <see cref="ContextBudget"/>.
/// </para>
/// <para>
/// Consistency matters more than perfect precision. A counter that applies the same accounting rules across both
/// preparation and compaction produces stable behavior even when the provider reports exact token usage later.
/// </para>
/// </remarks>
public interface ITokenCounter
{
    /// <summary>
    /// Counts the tokens for a single <see cref="ContextMessage"/>.
    /// </summary>
    /// <remarks>
    /// Implementations should apply the same counting rules used for multi-message totals so budget calculations remain
    /// internally consistent. Callers rely on this method when caching per-message counts on <see cref="ContextMessage.TokenCount"/>.
    /// </remarks>
    /// <param name="contextMessage">The message whose token cost should be measured.</param>
    /// <returns>The token count assigned to <paramref name="contextMessage"/>.</returns>
    int Count(ContextMessage contextMessage);

    /// <summary>
    /// Counts the tokens for a sequence of <see cref="ContextMessage"/> values.
    /// </summary>
    /// <remarks>
    /// This overload is used when TokenGuard needs the aggregate cost of a prepared or candidate history. Custom
    /// implementations should ensure the returned total matches the sum semantics callers would expect from repeated
    /// <see cref="Count(ContextMessage)"/> calls.
    /// </remarks>
    /// <param name="messages">The ordered messages whose combined token cost should be measured.</param>
    /// <returns>The total token count for <paramref name="messages"/>.</returns>
    int Count(IEnumerable<ContextMessage> messages);
}
