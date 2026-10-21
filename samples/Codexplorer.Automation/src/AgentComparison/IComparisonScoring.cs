namespace Codexplorer.Automation.AgentComparison;

/// <summary>Defines original per-check evaluation for comparison sessions.</summary>
internal interface IComparisonScoring
{
    /// <summary>Evaluates original checks independently of runtime telemetry.</summary>
    /// <param name="session">The frozen task contract.</param>
    /// <param name="artifactRoot">The authorized artifact directory.</param>
    /// <param name="answer">The available terminal answer.</param>
    /// <returns>The separate original populations.</returns>
    ComparisonCheckResults Score(ComparisonSessionPlan session, string artifactRoot, string? answer);
}
