namespace Codexplorer.Automation.AgentComparison;

/// <summary>Represents complete totals and separately retained known partial spend.</summary>
/// <param name="InputTokens">The complete input total, or null.</param>
/// <param name="OutputTokens">The complete output total, or null.</param>
/// <param name="CachedInputTokens">The complete cached subset total, or null.</param>
/// <param name="ReasoningOutputTokens">The complete reasoning subset total, or null.</param>
/// <param name="CacheWriteTokens">The complete reported cache-write total, or null.</param>
/// <param name="Cost">The complete reported cost, or null.</param>
/// <param name="KnownCost">The sum of available costs, including failed attempts.</param>
/// <param name="CompleteTokens">Whether every chargeable attempt reports both token counts.</param>
/// <param name="CompleteCost">Whether every chargeable attempt reports cost.</param>
/// <param name="AttemptCount">The number of observed attempts.</param>
/// <param name="FailedAttempts">The number of failed or unknown terminal attempts.</param>
/// <param name="UnknownUsageAttempts">The number of chargeable attempts with incomplete usage.</param>
/// <param name="UnclassifiedCalls">The number of conservatively charged unknown calls.</param>
/// <param name="BudgetRejectedAttempts">The number of requests denied before upstream forwarding.</param>
internal sealed record ComparisonUsageSummary(long? InputTokens, long? OutputTokens, long? CachedInputTokens,
    long? ReasoningOutputTokens, long? CacheWriteTokens, decimal? Cost, decimal? KnownCost, bool CompleteTokens,
    bool CompleteCost, int AttemptCount, int FailedAttempts, int UnknownUsageAttempts, int UnclassifiedCalls, int BudgetRejectedAttempts);
