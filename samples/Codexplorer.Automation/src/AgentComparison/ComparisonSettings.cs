using Codexplorer.Automation.Configuration;
using Codexplorer.Automation.Reporting;

namespace Codexplorer.Automation.AgentComparison;

/// <summary>Represents ComparisonSettings data for the captured comparison.</summary>
/// <param name="Model">The shared provider model slug.</param>
/// <param name="Provider">The pinned catalog provider identifier, or null when unresolved.</param>
/// <param name="MaxOutputTokens">The enforced output ceiling.</param>
/// <param name="ReasoningEnabled">The enforced reasoning setting.</param>
internal sealed record ComparisonSettings(
    string Model,
    string? Provider,
    int MaxOutputTokens,
    bool ReasoningEnabled)
{
    /// <summary>Gets the selected provider endpoint's supported context window.</summary>
    public int? ProviderContextTokens { get; init; }

    /// <summary>Gets the selected endpoint's supported maximum completion size.</summary>
    public int? ProviderMaxCompletionTokens { get; init; }
}
