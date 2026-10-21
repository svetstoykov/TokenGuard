using Codexplorer.Automation.Configuration;
using Codexplorer.Automation.Reporting;

namespace Codexplorer.Automation.AgentComparison;

/// <summary>Represents RuntimeResult data for the captured comparison.</summary>
/// <param name="Outcome">The completed, failed, cancelled or budgetExhausted outcome.</param>
/// <param name="Partial">Whether the deliverable is partial.</param>
/// <param name="FinalAnswer">The available terminal answer.</param>
/// <param name="AgentCalls">The observed main-agent request consumption.</param>
/// <param name="CapturePaths">The runtime evidence paths.</param>
/// <param name="Error">The redacted failure reason.</param>
internal sealed record RuntimeResult(
    string Outcome,
    bool Partial,
    string? FinalAnswer,
    int AgentCalls,
    IReadOnlyList<string> CapturePaths,
    string? Error);
