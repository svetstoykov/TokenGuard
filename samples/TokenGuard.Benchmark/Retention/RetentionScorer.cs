using System.Text.RegularExpressions;

namespace TokenGuard.Samples.Benchmark.Retention;

/// <summary>
/// Scores recall-probe model output against planted facts from a benchmark scenario.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RetentionScorer"/> is intentionally stateless so benchmark runners can create and discard instances freely
/// without carrying any per-run cache or provider-specific behavior. All scoring inputs arrive through
/// <see cref="Score(ScenarioProfile, string, int, int, string)"/>.
/// </para>
/// <para>
/// Parsing tolerates small formatting variations in model output by recognizing answer lines that start with `Q&lt;N&gt;:`,
/// `&lt;N&gt;.` or `&lt;N&gt;:` after trimming leading whitespace. Matching then uses case-insensitive substring checks so minor
/// phrasing around core fact value does not cause false negatives.
/// </para>
/// </remarks>
public sealed partial class RetentionScorer
{
    /// <summary>
    /// Scores model recall output for one synthesized benchmark scenario.
    /// </summary>
    /// <param name="profile">Scenario profile that defines planted fact order and expected answers. Cannot be <see langword="null"/>.</param>
    /// <param name="modelResponse">Raw model response to recall probe. Cannot be <see langword="null"/>.</param>
    /// <param name="baselineTokens">Token count for uncompacted baseline conversation. Must be greater than zero.</param>
    /// <param name="managedTokens">Token count for managed conversation under test. Cannot be negative.</param>
    /// <param name="strategyName">Human-readable strategy label for benchmark run. Cannot be null or whitespace.</param>
    /// <returns>
    /// A <see cref="RetentionResult"/> containing per-fact pass or fail detail, aggregate recall metrics, and token
    /// savings for supplied run.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="profile"/> or <paramref name="modelResponse"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="baselineTokens"/> is less than or equal to zero or when <paramref name="managedTokens"/> is negative.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="strategyName"/> is null or whitespace.</exception>
    public RetentionResult Score(
        ScenarioProfile profile,
        string modelResponse,
        int baselineTokens,
        int managedTokens,
        string strategyName)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(modelResponse);

        if (baselineTokens <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(baselineTokens), "Baseline tokens must be greater than zero.");
        }

        if (managedTokens < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(managedTokens), "Managed tokens cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(strategyName))
        {
            throw new ArgumentException("Strategy name cannot be null or whitespace.", nameof(strategyName));
        }

        var answersByQuestion = ParseAnswers(modelResponse);
        var factResults = new List<FactResult>(profile.Facts.Count);

        for (var index = 0; index < profile.Facts.Count; index++)
        {
            var fact = profile.Facts[index];
            answersByQuestion.TryGetValue(index + 1, out var actualAnswer);

            var passed = actualAnswer is not null &&
                actualAnswer.Contains(fact.GroundTruth, StringComparison.OrdinalIgnoreCase);

            factResults.Add(new FactResult(
                fact.Id,
                fact.Category,
                fact.GroundTruth,
                actualAnswer,
                passed));
        }

        var recalledFacts = factResults.Count(result => result.Passed);
        var totalFacts = profile.Facts.Count;
        double retentionScore = totalFacts == 0 ? 0.0 : recalledFacts / (double)totalFacts;
        double tokenSavingsPercent = 1.0 - (managedTokens / (double)baselineTokens);

        return new RetentionResult(
            profile.Name,
            strategyName,
            totalFacts,
            recalledFacts,
            retentionScore,
            baselineTokens,
            managedTokens,
            tokenSavingsPercent,
            factResults);
    }

    private static Dictionary<int, string> ParseAnswers(string modelResponse)
    {
        var answersByQuestion = new Dictionary<int, string>();

        foreach (var rawLine in modelResponse.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();

            if (line.Length == 0)
            {
                continue;
            }

            var match = AnswerLineRegex().Match(line);

            if (!match.Success)
            {
                continue;
            }

            var questionNumber = int.Parse(match.Groups["number"].Value);
            var answer = match.Groups["answer"].Value.Trim();

            answersByQuestion[questionNumber] = answer.Length == 0 ? string.Empty : answer;
        }

        return answersByQuestion;
    }

    [GeneratedRegex(@"^(?:Q\s*)?(?<number>\d+)\s*(?:[\.:])\s*(?<answer>.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AnswerLineRegex();
}
