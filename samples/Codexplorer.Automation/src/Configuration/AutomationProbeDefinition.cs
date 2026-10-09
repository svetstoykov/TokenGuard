namespace Codexplorer.Automation.Configuration;

/// <summary>Represents an opening-instruction retention probe.</summary>
internal sealed record AutomationProbeDefinition
{
    /// <summary>Gets the reference code.</summary>
    public string? Canary { get; init; }

    /// <summary>Gets the required compaction kind, or null for any kind.</summary>
    public string? Requires { get; init; }
}
