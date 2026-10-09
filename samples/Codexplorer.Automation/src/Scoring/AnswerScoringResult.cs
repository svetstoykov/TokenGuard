using Codexplorer.Automation.Reporting;

namespace Codexplorer.Automation.Scoring;

/// <summary>Represents ordered checks and an optional probe result.</summary>
internal sealed record AnswerScoringResult
{
    /// <summary>Gets the non-null ordered check results.</summary>
    public required IReadOnlyList<CheckResult> Checks { get; init; }

    /// <summary>Gets the probe result, or null when undeclared.</summary>
    public required ProbeResult? Probe { get; init; }
}
