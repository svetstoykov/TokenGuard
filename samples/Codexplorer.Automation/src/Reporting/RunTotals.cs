namespace Codexplorer.Automation.Reporting;

/// <summary>
///     Represents task completion counts and the combined run measurement population.
/// </summary>
/// <remarks>Estimator statistics use all eligible paired observations rather than averaging task statistics.</remarks>
internal sealed record RunTotals
{
    /// <summary>
    ///     Gets the number of started tasks.
    /// </summary>
    public required int TaskCount { get; init; }

    /// <summary>
    ///     Gets the number of protocol-completed tasks.
    /// </summary>
    public required int ProtocolCompletedTaskCount { get; init; }

    /// <summary>
    ///     Gets the protocol completion ratio, or <see langword="null" /> when no task started.
    /// </summary>
    public required double? ProtocolCompletionRate { get; init; }

    /// <summary>
    ///     Gets the aggregated numeric measurements.
    /// </summary>
    public required ReportMetrics Metrics { get; init; }
}
