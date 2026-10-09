using Codexplorer.Automation.Configuration;
using Codexplorer.Automation.Reporting;

namespace Codexplorer.Automation.Scoring;

/// <summary>Defines deterministic scoring of validated declarations.</summary>
internal interface IAnswerScorer
{
    /// <summary>Scores available task-finalization evidence in declaration order.</summary>
    /// <param name="checks">The non-null validated check declarations.</param>
    /// <param name="probe">The optional validated probe.</param>
    /// <param name="input">The non-null evidence.</param>
    /// <returns>The ordered check verdicts and optional probe verdict.</returns>
    AnswerScoringResult Score(IReadOnlyList<AutomationCheckDefinition> checks, AutomationProbeDefinition? probe, ScoringInput input);

    /// <summary>Evaluates one validated declaration against a supplied text.</summary>
    /// <param name="check">The validated declaration.</param>
    /// <param name="text">The text, or null when unavailable.</param>
    /// <param name="unavailableReason">The noAnswer or artifactMissing reason.</param>
    /// <returns>The deterministic verdict.</returns>
    CheckResult EvaluateCheck(AutomationCheckDefinition check, string? text, string unavailableReason);
}
