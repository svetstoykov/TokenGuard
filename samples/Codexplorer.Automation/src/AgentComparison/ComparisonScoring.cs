using Codexplorer.Automation.Scoring;
using Codexplorer.Automation.Reporting;

namespace Codexplorer.Automation.AgentComparison;

/// <summary>Uses the original evaluator for each original task check.</summary>
/// <remarks>This per-run stateless service uses filesystem evidence scoped to one session.</remarks>
internal sealed class ComparisonScoring(IAnswerScorer scorer) : IComparisonScoring
{
    /// <inheritdoc />
    public ComparisonCheckResults Score(ComparisonSessionPlan session, string artifactRoot, string? answer)
    {
        if (session.Task.Probe is not null)
            throw new InvalidDataException("Comparison schema 1 rejects probe-bearing tasks.");
        var inventory = ComparisonArtifacts.Inventory(artifactRoot);
        var evidence = new List<CheckResult>();
        var finalAnswer = new List<CheckResult>();
        foreach (var check in session.Task.Checks ?? [])
        {
            if (check.Artifact is null)
            {
                finalAnswer.Add(scorer.EvaluateCheck(check, answer, "noAnswer"));
                continue;
            }
            ComparisonArtifacts.Resolve(artifactRoot, check.Artifact);
            var selected = AutomationRunner.SelectArtifactPath(check.Artifact, inventory.Select(file => file.Path));
            var text = selected is null ? null : File.ReadAllText(ComparisonArtifacts.Resolve(artifactRoot, selected));
            evidence.Add(scorer.EvaluateCheck(check, text, "artifactMissing"));
        }
        return new ComparisonCheckResults(evidence, finalAnswer);
    }
}
