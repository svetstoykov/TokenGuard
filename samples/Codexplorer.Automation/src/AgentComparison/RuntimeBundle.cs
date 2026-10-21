using Codexplorer.Automation.Configuration;
using Codexplorer.Automation.Reporting;

namespace Codexplorer.Automation.AgentComparison;

/// <summary>Represents RuntimeBundle data for the captured comparison.</summary>
/// <param name="Name">The runtime identity.</param>
/// <param name="Executable">The absolute retained executable path.</param>
/// <param name="Version">The verified runtime version.</param>
/// <param name="Files">The full retained bundle inventory.</param>
internal sealed record RuntimeBundle(
    string Name,
    string Executable,
    string Version,
    IReadOnlyList<FileDigest> Files)
{
    /// <summary>Gets the source revision used to build this runtime.</summary>
    public string? SourceSha { get; init; }

    /// <summary>Gets a value indicating whether the runtime includes uncommitted source changes.</summary>
    public bool Dirty { get; init; }
}
