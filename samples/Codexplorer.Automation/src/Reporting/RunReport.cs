namespace Codexplorer.Automation.Reporting;

/// <summary>
///     Represents a versioned machine-readable automation run report.
/// </summary>
/// <remarks>The report preserves partial task coverage and explicitly separates protocol completion from deliverable evaluation.</remarks>
internal sealed record RunReport
{
    /// <summary>
    ///     Gets the report schema version.
    /// </summary>
    public required int SchemaVersion { get; init; }

    /// <summary>
    ///     Gets the reproducibility metadata.
    /// </summary>
    public required RunMetadata Run { get; init; }

    /// <summary>
    ///     Gets the completed and active task records.
    /// </summary>
    public required IReadOnlyList<TaskReport> Tasks { get; init; }

    /// <summary>
    ///     Gets the run totals.
    /// </summary>
    public required RunTotals Totals { get; init; }

    /// <summary>
    ///     Gets a value indicating whether the run contains failures or incomplete coverage.
    /// </summary>
    public required bool Partial { get; init; }

    /// <summary>
    ///     Gets the manifest task identifiers that never started.
    /// </summary>
    public required IReadOnlyList<string> UnrunTaskIds { get; init; }

    /// <summary>
    ///     Gets the measurement and control validation result.
    /// </summary>
    public required ReportValidation Validation { get; init; }

    /// <summary>
    ///     Gets the estimator error formula, ratio unit, and sign convention.
    /// </summary>
    public string EstimatorErrorUnit => "ratio: (providerInput - tokensAfter) / providerInput; positive means underestimation";
}
