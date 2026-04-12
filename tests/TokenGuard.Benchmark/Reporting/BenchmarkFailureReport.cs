using TokenGuard.Benchmark.AgentWorkflow.Models;

namespace TokenGuard.Benchmark.Reporting;

/// <summary>
/// Represents persisted diagnostic details for failed benchmark runs.
/// </summary>
public sealed record BenchmarkFailureReport(
    string RunId,
    string TaskName,
    string ConfigurationName,
    string Mode,
    string Model,
    string WorkspaceDirectory,
    DateTimeOffset OccurredAtUtc,
    int CompletedTurns,
    int TotalInputTokens,
    int TotalOutputTokens,
    int CompactionEvents,
    int MaxIterations,
    int? MaxTokens,
    double? CompactionThreshold,
    TurnTelemetry? LastTurn,
    BenchmarkFailureException Exception);

/// <summary>
/// Represents serializable exception details for benchmark failure diagnostics.
/// </summary>
public sealed record BenchmarkFailureException(
    string Type,
    string Message,
    string? StackTrace,
    IReadOnlyDictionary<string, string?> Properties,
    BenchmarkFailureException? InnerException);
