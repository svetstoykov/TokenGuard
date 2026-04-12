namespace TokenGuard.Benchmark.AgentWorkflow.Models;

/// <summary>
/// Represents derived comparison metrics between raw and managed runs.
/// </summary>
/// <param name="InputTokenSavingsPercent">Percentage reduction in cumulative input tokens from raw to managed run.</param>
/// <param name="TotalInputTokensRaw">Total raw-run input tokens.</param>
/// <param name="TotalInputTokensManaged">Total managed-run input tokens.</param>
/// <param name="TurnCountRaw">Turn count for raw run.</param>
/// <param name="TurnCountManaged">Turn count for managed run.</param>
/// <param name="BothCompleted">Indicates whether both configurations completed task.</param>
public sealed record BenchmarkComparison(
    double InputTokenSavingsPercent,
    int TotalInputTokensRaw,
    int TotalInputTokensManaged,
    int TurnCountRaw,
    int TurnCountManaged,
    bool BothCompleted);
