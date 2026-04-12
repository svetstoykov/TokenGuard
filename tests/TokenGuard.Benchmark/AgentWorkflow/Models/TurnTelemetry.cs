namespace TokenGuard.Benchmark.AgentWorkflow.Models;

/// <summary>
/// Represents per-turn benchmark telemetry captured during one run.
/// </summary>
/// <param name="Turn">One-based turn number.</param>
/// <param name="InputTokens">Provider-reported input tokens for turn.</param>
/// <param name="OutputTokens">Provider-reported output tokens for turn.</param>
/// <param name="CumulativeInputTokens">Running sum of provider-reported input tokens.</param>
/// <param name="Compacted">Indicates whether compaction was active for turn.</param>
/// <param name="MaskedCount">Number of masked messages present in prepared request.</param>
/// <param name="DurationMs">Wall-clock duration for turn in milliseconds.</param>
/// <param name="FinishReason">Provider finish reason for turn.</param>
public sealed record TurnTelemetry(
    int Turn,
    int? InputTokens,
    int? OutputTokens,
    int CumulativeInputTokens,
    bool Compacted,
    int MaskedCount,
    long DurationMs,
    string FinishReason);
