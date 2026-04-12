namespace TokenGuard.Benchmark.Models;

/// <summary>
/// Represents one benchmark configuration executed by benchmark runner.
/// </summary>
/// <param name="Name">Display name written to console output and reports.</param>
/// <param name="Mode">Conversation-management mode used for run.</param>
/// <param name="MaxTokens">Maximum context budget applied to managed runs.</param>
/// <param name="CompactionThreshold">Threshold fraction that triggers compaction for managed runs.</param>
/// <param name="MaxIterations">Maximum turns allowed before run is marked incomplete.</param>
public sealed record BenchmarkConfiguration(
    string Name,
    BenchmarkMode Mode,
    int? MaxTokens,
    double? CompactionThreshold,
    int MaxIterations)
{
    /// <summary>
    /// Gets built-in raw configuration used for A/B comparison.
    /// </summary>
    public static BenchmarkConfiguration Raw { get; } = new(
        Name: "Raw",
        Mode: BenchmarkMode.Raw,
        MaxTokens: null,
        CompactionThreshold: null,
        MaxIterations: 50);

    /// <summary>
    /// Gets built-in TokenGuard sliding-window configuration used for A/B comparison.
    /// </summary>
    public static BenchmarkConfiguration SlidingWindow { get; } = new(
        Name: "SlidingWindow",
        Mode: BenchmarkMode.SlidingWindow,
        MaxTokens: 80_000,
        CompactionThreshold: 0.80,
        MaxIterations: 50);
}
