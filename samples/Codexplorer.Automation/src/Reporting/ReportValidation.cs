namespace Codexplorer.Automation.Reporting;

/// <summary>
///     Represents the validity of collected measurements.
/// </summary>
/// <remarks>Errors contain sanitized invariant descriptions suitable for inclusion in reports.</remarks>
internal sealed record ReportValidation
{
    /// <summary>
    ///     Gets a value indicating whether collection satisfies the report and control invariants.
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    ///     Gets the sanitized validation error descriptions.
    /// </summary>
    public required IReadOnlyList<string> Errors { get; init; }
}
