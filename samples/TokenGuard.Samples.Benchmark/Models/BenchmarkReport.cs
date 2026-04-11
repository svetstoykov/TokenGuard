namespace TokenGuard.Samples.Benchmark.Models;

/// <summary>
/// Represents persisted JSON report for one benchmark task execution.
/// </summary>
/// <param name="Task">Task name benchmarked in report.</param>
/// <param name="Model">Model identifier used for all runs.</param>
/// <param name="Timestamp">UTC timestamp when report was created.</param>
/// <param name="Runs">Run results for each benchmark configuration.</param>
/// <param name="Comparison">Derived comparison metrics across runs.</param>
public sealed record BenchmarkReport(
    string Task,
    string Model,
    DateTimeOffset Timestamp,
    IReadOnlyList<RunResult> Runs,
    BenchmarkComparison Comparison);
