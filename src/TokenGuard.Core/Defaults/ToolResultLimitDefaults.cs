using TokenGuard.Core.Models;

namespace TokenGuard.Core.Defaults;

/// <summary>
/// Defines the library's default limit on the estimated tokens of one recorded tool result.
/// </summary>
/// <remarks>
/// The default limit is the smaller of <see cref="MaxTokens"/> and <see cref="ContextBudget.MaxTokens"/> divided by
/// <see cref="BudgetDivisor"/>, and never less than one token: 10,000 tokens at the default 40,000-token budget and
/// 25,000 tokens from a 100,000-token budget upward.
/// </remarks>
internal static class ToolResultLimitDefaults
{
    /// <summary>
    /// Gets the largest default limit, in estimated tokens.
    /// </summary>
    internal const int MaxTokens = 25_000;

    /// <summary>
    /// Gets the divisor applied to the budget's maximum token count to size the default limit for small budgets.
    /// </summary>
    internal const int BudgetDivisor = 4;

    /// <summary>
    /// Returns the default tool result limit for a budget.
    /// </summary>
    /// <param name="maxTokens">The maximum token count of the budget.</param>
    /// <returns>The default limit in estimated tokens, at least one.</returns>
    internal static int Resolve(int maxTokens) => Math.Max(1, Math.Min(MaxTokens, maxTokens / BudgetDivisor));
}
