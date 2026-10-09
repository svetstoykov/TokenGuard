using System.Text.RegularExpressions;
using Codexplorer.Automation.Configuration;
using Codexplorer.Automation.Reporting;

namespace Codexplorer.Automation.Scoring;

/// <summary>Evaluates explicit text checks and retention probes.</summary>
/// <remarks><para>This stateless singleton is thread-safe and uses only supplied evidence.</para></remarks>
internal sealed class AnswerScorer : IAnswerScorer
{
    /// <summary>Defines the deterministic pattern engine options.</summary>
    internal const RegexOptions PatternOptions = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking;

    /// <inheritdoc />
    public AnswerScoringResult Score(IReadOnlyList<AutomationCheckDefinition> checks, AutomationProbeDefinition? probe, ScoringInput input)
    {
        var answer = string.IsNullOrWhiteSpace(input.FinalAnswer) ? null : input.FinalAnswer;
        var results = checks.Select(check => this.EvaluateCheck(check,
            check.Artifact is null ? answer : input.ArtifactTexts.GetValueOrDefault(check.Artifact),
            check.Artifact is null ? "noAnswer" : "artifactMissing")).ToArray();
        ProbeResult? result = null;
        if (probe is not null)
        {
            var present = answer is not null && ValueMatcher.IsMatch(TextNormalizer.Normalize(answer), TextNormalizer.Normalize(probe.Canary!));
            var reason = answer is null ? "noAnswer" : input.CanaryRepeated ? "canaryRepeated"
                : ProbeValidity.GetInvalidReason(probe.Requires, input.Measurements);
            result = new ProbeResult
            {
                Requires = probe.Requires, CanaryPresent = present,
                Status = reason is not null ? "invalid" : present ? "passed" : "failed",
                Reason = reason ?? (present ? null : "canaryMissing"),
            };
        }
        return new AnswerScoringResult { Checks = results, Probe = result };
    }

    /// <inheritdoc />
    public CheckResult EvaluateCheck(AutomationCheckDefinition check, string? text, string unavailableReason)
    {
        if (text is null || (unavailableReason == "noAnswer" && string.IsNullOrWhiteSpace(text)))
            return new CheckResult { Id = check.Id!, Passed = false, Reason = unavailableReason };
        var normalized = TextNormalizer.Normalize(text);
        var found = check.Kind == "contains"
            ? check.AnyOf!.Any(value => ValueMatcher.IsMatch(normalized, TextNormalizer.Normalize(value)))
            : new Regex(check.Pattern!, PatternOptions, Regex.InfiniteMatchTimeout).IsMatch(normalized);
        var reason = !found ? "notFound"
            : check.NoneOf?.Any(value => ValueMatcher.IsMatch(normalized, TextNormalizer.Normalize(value))) == true
                ? "forbiddenValuePresent" : null;
        return new CheckResult { Id = check.Id!, Passed = reason is null, Reason = reason };
    }
}
