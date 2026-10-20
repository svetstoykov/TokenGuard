namespace Codexplorer.Automation.Reporting;

/// <summary>
///     Represents task completion counts and the combined run measurement population.
/// </summary>
/// <remarks>Estimator statistics use all eligible paired observations rather than averaging task statistics.</remarks>
internal sealed record RunTotals
{
    /// <summary>Gets the separate masked-result population, or null for reports before schema version 7.</summary>
    public MaskedResultProbeTotals? MaskedResultProbes { get; init; }

    /// <summary>Gets the tasks with checks.</summary>
    public required int EvaluatedTaskCount { get; init; }

    /// <summary>Gets the tasks with all checks passed.</summary>
    public required int DeliverableCompletedTaskCount { get; init; }

    /// <summary>Gets the completed fraction among evaluated tasks.</summary>
    public required double? DeliverableCompletionRate { get; init; }

    /// <summary>Gets the declared probe count.</summary>
    public required int ProbeCount { get; init; }

    /// <summary>Gets the invalid probe count.</summary>
    public required int InvalidProbeCount { get; init; }

    /// <summary>
    ///     Gets the probes of tasks where nothing was summarized or dropped, or <see langword="null" /> in a report whose schema version
    ///     is below 6 and so counts none.
    /// </summary>
    public int? NotApplicableProbeCount { get; init; }

    /// <summary>Gets the passed probe count.</summary>
    public required int PassedProbeCount { get; init; }

    /// <summary>Gets the passed fraction among valid probes.</summary>
    public required double? ProbePassRate { get; init; }

    /// <summary>Gets the probes containing the code, including invalid probes.</summary>
    public required int CanaryPresentCount { get; init; }


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
