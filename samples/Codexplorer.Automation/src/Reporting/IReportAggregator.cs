using Codexplorer.Measurements;

namespace Codexplorer.Automation.Reporting;

/// <summary>
///     Defines pure aggregation of cumulative task measurements.
/// </summary>
/// <remarks>Implementations derive statistics from paired records and preserve unavailable usage.</remarks>
internal interface IReportAggregator
{
    /// <summary>
    ///     Builds one task report from its cumulative measurements.
    /// </summary>
    /// <param name="taskId">The manifest task identifier.</param>
    /// <param name="size">The configured task size.</param>
    /// <param name="outcome">The terminal protocol outcome.</param>
    /// <param name="protocolCompletion">Whether an in-budget final wrap-up reply was received.</param>
    /// <param name="budget">The positive task model-call allowance.</param>
    /// <param name="measurements">The cumulative session snapshot. Cannot be <see langword="null" />.</param>
    /// <param name="helperResponses">The usage of received helper responses. Cannot be <see langword="null" />.</param>
    /// <param name="helperCalls">The number of started helper attempts.</param>
    /// <param name="logPath">The session log path, or <see langword="null" /> when no log was created.</param>
    /// <returns>The task report with measurements aggregated from the snapshot.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="measurements" /> is <see langword="null" />.</exception>
    TaskReport CreateTask(string taskId, string size, string outcome, bool protocolCompletion, int budget,
        SessionMeasurements measurements, IReadOnlyList<UsageMeasurement> helperResponses, long helperCalls, string? logPath);

    /// <summary>
    ///     Combines task populations into a versioned run report.
    /// </summary>
    /// <param name="metadata">The run provenance and effective settings. Cannot be <see langword="null" />.</param>
    /// <param name="tasks">The started task reports. Cannot be <see langword="null" />.</param>
    /// <param name="unrunTaskIds">The identifiers of manifest tasks that never started. Cannot be <see langword="null" />.</param>
    /// <param name="partial">Whether run execution ended with incomplete coverage.</param>
    /// <returns>The report with combined populations, partial coverage, and validation information.</returns>
    RunReport CreateReport(RunMetadata metadata, IReadOnlyList<TaskReport> tasks, IReadOnlyList<string> unrunTaskIds, bool partial);
}
