namespace TokenGuard.Benchmark.Models;

/// <summary>
/// Represents full result of one benchmark configuration run.
/// </summary>
/// <param name="Configuration">Configuration name that produced run.</param>
/// <param name="Completed">Indicates whether task reached completion marker.</param>
/// <param name="TurnCount">Number of turns attempted.</param>
/// <param name="TotalInputTokens">Sum of provider-reported input tokens across all turns.</param>
/// <param name="TotalOutputTokens">Sum of provider-reported output tokens across all turns.</param>
/// <param name="CompactionEvents">Number of turns where masked content was present.</param>
/// <param name="DurationMs">Total wall-clock duration of run in milliseconds.</param>
/// <param name="Turns">Per-turn telemetry captured during run.</param>
/// <param name="FailureReason">Optional terminal failure reason when run did not complete successfully.</param>
public sealed record RunResult(
    string Configuration,
    bool Completed,
    int TurnCount,
    int TotalInputTokens,
    int TotalOutputTokens,
    int CompactionEvents,
    long DurationMs,
    IReadOnlyList<TurnTelemetry> Turns,
    string? FailureReason);
