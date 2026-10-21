using Codexplorer.Automation.AgentComparison.Recording;

namespace Codexplorer.Automation.AgentComparison;

/// <summary>Provides nullable per-attempt usage aggregation without adding subset counters.</summary>
internal static class ComparisonAccounting
{
    /// <summary>Aggregates successful and failed chargeable attempts while preserving unknown totals.</summary>
    /// <param name="attempts">The independently captured HTTP attempts.</param>
    /// <returns>The complete and known partial totals.</returns>
    internal static ComparisonUsageSummary Aggregate(IReadOnlyList<RecordedAttempt> attempts)
    {
        var chargeable = attempts.Where(attempt => !attempt.BudgetRejected).ToArray();
        var usage = chargeable.Select(attempt => attempt.Usage).ToArray();
        var cost = usage.Select(item => item?.Cost).ToArray();
        var completeCost = cost.Length > 0 && cost.All(value => value.HasValue) && usage.All(item => item?.Complete == true);
        var knownCost = cost.Any(value => value.HasValue) ? cost.Where(value => value.HasValue).Sum(value => value!.Value) : (decimal?)null;
        return new ComparisonUsageSummary(Sum(usage.Select(item => item?.InputTokens)), Sum(usage.Select(item => item?.OutputTokens)),
            Sum(usage.Select(item => item?.CachedInputTokens)), Sum(usage.Select(item => item?.ReasoningOutputTokens)),
            Sum(usage.Select(item => item?.CacheWriteTokens)), completeCost ? knownCost : null,
            knownCost, usage.Length > 0 && usage.All(item => item?.Complete == true),
            completeCost, attempts.Count,
            chargeable.Count(attempt => attempt.Error is not null || attempt.StatusCode is null or >= 400),
            usage.Count(item => item?.Complete != true), chargeable.Count(attempt => attempt.Kind == RecordedRequestKind.Unknown),
            attempts.Count(attempt => attempt.BudgetRejected));
    }

    private static long? Sum(IEnumerable<long?> source)
    {
        var values = source.ToArray();
        return values.Length > 0 && values.All(value => value.HasValue) ? values.Sum(value => value!.Value) : null;
    }
}
