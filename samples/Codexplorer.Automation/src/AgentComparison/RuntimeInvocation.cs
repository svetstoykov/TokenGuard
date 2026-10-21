using Codexplorer.Automation.Configuration;
using Codexplorer.Automation.Reporting;

namespace Codexplorer.Automation.AgentComparison;

/// <summary>Represents RuntimeInvocation data for the captured comparison.</summary>
/// <param name="Session">The immutable session contract.</param>
/// <param name="Executable">The retained runtime executable.</param>
/// <param name="Checkout">The isolated repository checkout.</param>
/// <param name="Artifacts">The isolated external artifact directory.</param>
/// <param name="StateRoot">The isolated configuration and home root.</param>
/// <param name="RecorderEndpoint">The loopback OpenAI-compatible endpoint.</param>
/// <param name="LocalCredential">The disposable child credential.</param>
/// <param name="Settings">The shared effective upstream settings.</param>
/// <param name="Synthetic">Whether all upstream traffic is fake.</param>
internal sealed record RuntimeInvocation(
    ComparisonSessionPlan Session,
    string Executable,
    string Checkout,
    string Artifacts,
    string StateRoot,
    Uri RecorderEndpoint,
    string LocalCredential,
    ComparisonSettings Settings,
    bool Synthetic)
{
    /// <summary>Gets the disposable credential reserved for positively identified Codexplorer summaries.</summary>
    public string? SummarizerCredential { get; init; }
}
