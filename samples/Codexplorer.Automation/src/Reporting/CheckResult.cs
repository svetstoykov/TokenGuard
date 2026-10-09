namespace Codexplorer.Automation.Reporting;

/// <summary>Represents a deterministic check verdict.</summary>
internal sealed record CheckResult
{
    /// <summary>Gets the check identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Gets a value indicating whether the check passed.</summary>
    public required bool Passed { get; init; }

    /// <summary>Gets the failure reason, or null for a pass.</summary>
    public required string? Reason { get; init; }
}
