namespace Codexplorer.Automation.Reporting;

/// <summary>Represents a retention probe verdict.</summary>
internal sealed record ProbeResult
{
    /// <summary>Gets the required compaction kind.</summary>
    public required string? Requires { get; init; }

    /// <summary>Gets the passed, failed or invalid status.</summary>
    public required string Status { get; init; }

    /// <summary>Gets the reason, or null for a pass.</summary>
    public required string? Reason { get; init; }

    /// <summary>Gets a value indicating whether the final answer contained the code.</summary>
    public required bool CanaryPresent { get; init; }
}
