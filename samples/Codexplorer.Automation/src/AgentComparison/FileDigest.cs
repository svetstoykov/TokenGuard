using Codexplorer.Automation.Configuration;
using Codexplorer.Automation.Reporting;

namespace Codexplorer.Automation.AgentComparison;

/// <summary>Represents FileDigest data for the captured comparison.</summary>
/// <param name="Path">The retained file path.</param>
/// <param name="Sha256">The SHA-256 of the file bytes.</param>
internal sealed record FileDigest(
    string Path,
    string Sha256);
