using Codexplorer.Measurements;

namespace Codexplorer.Automation.Reporting;

/// <summary>
///     Represents the result and measurements of one started manifest task.
/// </summary>
/// <remarks>Paired records retain their individual turn mapping and contain only measurement values.</remarks>
internal sealed record TaskReport
{
    /// <summary>
    ///     Gets the manifest task identifier.
    /// </summary>
    public required string TaskId { get; init; }

    /// <summary>
    ///     Gets the configured task size.
    /// </summary>
    public required string Size { get; init; }

    /// <summary>
    ///     Gets the terminal protocol outcome.
    /// </summary>
    public required string Outcome { get; init; }

    /// <summary>
    ///     Gets a value indicating whether the task received an in-budget final wrap-up reply.
    /// </summary>
    public required bool ProtocolCompletion { get; init; }

    /// <summary>
    ///     Gets the explicit <c>notEvaluated</c> deliverable status.
    /// </summary>
    public required string DeliverableCompletion { get; init; }

    /// <summary>
    ///     Gets the task model-call allowance.
    /// </summary>
    public required int ModelCallBudget { get; init; }

    /// <summary>
    ///     Gets a value indicating whether the session snapshot was taken after disposal.
    /// </summary>
    public required bool MeasurementsComplete { get; init; }

    /// <summary>
    ///     Gets the matched, mismatched, unavailable, or pending telemetry cross-check status.
    /// </summary>
    public required string SummaryCrossCheck { get; init; }

    /// <summary>
    ///     Gets the session log path, or <see langword="null" /> when no session log was created.
    /// </summary>
    public required string? SessionLogPath { get; init; }

    /// <summary>
    ///     Gets the measured turn offset, or <see langword="null" /> when absent or varying between pairs.
    /// </summary>
    public required int? TokenGuardTranscriptOffset { get; init; }

    /// <summary>
    ///     Gets every completed or incomplete prepare record.
    /// </summary>
    public required IReadOnlyList<PrepareMeasurement> PrepareRecords { get; init; }

    /// <summary>
    ///     Gets every started provider attempt and prepare mapping.
    /// </summary>
    public required IReadOnlyList<ProviderCallMeasurement> ProviderCalls { get; init; }

    /// <summary>
    ///     Gets the usage of received summarizer responses, including empty answers.
    /// </summary>
    public required IReadOnlyList<UsageMeasurement> SummarizerResponses { get; init; }

    /// <summary>
    ///     Gets the usage of received helper responses, including empty answers.
    /// </summary>
    public required IReadOnlyList<UsageMeasurement> HelperResponses { get; init; }

    /// <summary>
    ///     Gets the aggregated numeric measurements.
    /// </summary>
    public required ReportMetrics Metrics { get; init; }
}
