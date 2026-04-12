using System.Text;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;

namespace TokenGuard.Benchmarks.Retention;

/// <summary>
/// Synthesizes deterministic retention benchmark conversations from <see cref="ScenarioProfile"/> definitions.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ConversationSynthesizer"/> converts declarative scenario inputs into alternating user and model messages
/// that can be replayed directly through benchmark runners. It weaves planted facts into otherwise realistic noise while
/// using a supplied <see cref="ITokenCounter"/> to keep total output near the requested token budget.
/// </para>
/// <para>
/// Turn indices in <see cref="ScenarioProfile.Facts"/> are interpreted as user and assistant turn-pair indices. Each pair
/// produces exactly two messages, with user content first and model content second, so a profile with N turns always
/// yields <c>N * 2</c> messages.
/// </para>
/// </remarks>
public sealed class ConversationSynthesizer
{
    private const double MinVariance = 0.85;
    private const double MaxVariance = 1.15;

    private readonly ITokenCounter _tokenCounter;

    /// <summary>
    /// Initializes a new <see cref="ConversationSynthesizer"/> instance.
    /// </summary>
    /// <param name="tokenCounter">
    /// Token counter used to estimate message size as synthesis proceeds. This should match the benchmark environment as
    /// closely as practical so generated conversations land near the intended token budget.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="tokenCounter"/> is <see langword="null"/>.</exception>
    public ConversationSynthesizer(ITokenCounter tokenCounter)
    {
        this._tokenCounter = tokenCounter ?? throw new ArgumentNullException(nameof(tokenCounter));
    }

    /// <summary>
    /// Synthesizes a deterministic benchmark conversation for the supplied profile.
    /// </summary>
/// <param name="profile">Scenario definition to synthesize. Cannot be <see langword="null"/>.</param>
/// <returns>
/// A <see cref="SyntheticConversation"/> containing alternating conversation messages, formatted recall probe text,
/// and estimated total token count.
/// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="profile"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when a planted fact refers to an invalid turn index, when a relational dependency cannot be resolved, or
/// when a superseded fact update falls outside the profile turn count.
/// </exception>
    public SyntheticConversation Synthesize(ScenarioProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        ValidateProfile(profile);

        var random = new Random(profile.Seed);
        var templates = NoiseTemplates.GetTemplates(profile.NoiseStyle);
        var factsById = profile.Facts.ToDictionary(fact => fact.Id, StringComparer.Ordinal);
        var messages = new List<ContextMessage>(profile.TurnCount * 2);
        var targetTokensPerMessage = Math.Max(20, profile.TargetTokenCount / Math.Max(1, profile.TurnCount * 2));

        for (var turnIndex = 0; turnIndex < profile.TurnCount; turnIndex++)
        {
            var userSegments = new List<string>();
            var modelSegments = new List<string>();

            AddNoiseSegment(userSegments, templates, random);
            AddNoiseSegment(modelSegments, templates, random);

            foreach (var fact in profile.Facts.Where(fact => fact.PlantedAtTurn == turnIndex))
            {
                AddFactAtPlantTurn(fact, factsById, turnIndex, profile.TurnCount, random, userSegments, modelSegments);
            }

            foreach (var fact in profile.Facts.Where(fact => fact.Category == FactCategory.Superseded && fact.SupersededAtTurn == turnIndex))
            {
                AddSupersededUpdate(fact, random, userSegments, modelSegments);
            }

            foreach (var fact in profile.Facts.Where(fact => fact.Category == FactCategory.Reinforced))
            {
                AddReinforcementIfNeeded(fact, turnIndex, profile.TurnCount, random, userSegments, modelSegments);
            }

            var userMessage = BuildMessage(MessageRole.User, userSegments, templates, random, targetTokensPerMessage);
            var modelMessage = BuildMessage(MessageRole.Model, modelSegments, templates, random, targetTokensPerMessage);

            messages.Add(userMessage);
            messages.Add(modelMessage);
        }

        var recallProbe = BuildRecallProbe(profile.Facts);
        var estimatedTokenCount = this._tokenCounter.Count(messages);

        return new SyntheticConversation(profile, messages, recallProbe, estimatedTokenCount);
    }

    private static void ValidateProfile(ScenarioProfile profile)
    {
        foreach (var fact in profile.Facts)
        {
            if (fact.PlantedAtTurn >= profile.TurnCount)
            {
                throw new ArgumentException(
                    $"Fact '{fact.Id}' has planted turn {fact.PlantedAtTurn}, which exceeds profile turn count {profile.TurnCount}.",
                    nameof(profile));
            }

            if (fact.SupersededAtTurn is >= 0 && fact.SupersededAtTurn.Value >= profile.TurnCount)
            {
                throw new ArgumentException(
                    $"Fact '{fact.Id}' has superseded turn {fact.SupersededAtTurn.Value}, which exceeds profile turn count {profile.TurnCount}.",
                    nameof(profile));
            }
        }

        var factIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var fact in profile.Facts)
        {
            if (!factIds.Add(fact.Id))
            {
                throw new ArgumentException($"Profile contains duplicate fact id '{fact.Id}'.", nameof(profile));
            }
        }

        foreach (var fact in profile.Facts.Where(fact => fact.Category == FactCategory.Relational))
        {
            if (!profile.Facts.Any(candidate => string.Equals(candidate.Id, fact.DependsOn, StringComparison.Ordinal)))
            {
                throw new ArgumentException(
                    $"Relational fact '{fact.Id}' depends on missing fact id '{fact.DependsOn}'.",
                    nameof(profile));
            }
        }
    }

    private static void AddFactAtPlantTurn(
        PlantedFact fact,
        IReadOnlyDictionary<string, PlantedFact> factsById,
        int turnIndex,
        int turnCount,
        Random random,
        List<string> userSegments,
        List<string> modelSegments)
    {
        switch (fact.Category)
        {
            case FactCategory.Anchor:
                AddSegmentToDeterministicSpeaker(
                    turnIndex,
                    userSegments,
                    modelSegments,
                    BuildAnchorSentence(fact, random));
                break;

            case FactCategory.Reinforced:
                AddSegmentToDeterministicSpeaker(
                    turnIndex,
                    userSegments,
                    modelSegments,
                    BuildReinforcedPrimarySentence(fact, random));
                break;

            case FactCategory.Superseded:
                AddSegmentToDeterministicSpeaker(
                    turnIndex,
                    userSegments,
                    modelSegments,
                    BuildSupersededInitialSentence(fact, random));
                break;

            case FactCategory.Relational:
                AddSegmentToDeterministicSpeaker(
                    turnIndex,
                    userSegments,
                    modelSegments,
                    BuildRelationalSentence(fact, factsById[fact.DependsOn!], random));
                break;

            case FactCategory.Buried:
                AddSegmentToDeterministicSpeaker(
                    turnIndex,
                    userSegments,
                    modelSegments,
                    BuildBuriedParagraph(fact, random));
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(fact.Category), fact.Category, "Unknown fact category.");
        }
    }

    private static void AddSupersededUpdate(PlantedFact fact, Random random, List<string> userSegments, List<string> modelSegments)
    {
        AddSegmentToDeterministicSpeaker(
            fact.SupersededAtTurn!.Value,
            userSegments,
            modelSegments,
            BuildSupersededFinalSentence(fact, random));
    }

    private static void AddReinforcementIfNeeded(
        PlantedFact fact,
        int currentTurn,
        int turnCount,
        Random random,
        List<string> userSegments,
        List<string> modelSegments)
    {
        var reinforcementTurns = GetReinforcementTurns(fact, turnCount);

        if (!reinforcementTurns.Contains(currentTurn))
        {
            return;
        }

        AddSegmentToDeterministicSpeaker(
            currentTurn,
            userSegments,
            modelSegments,
            BuildReinforcementSentence(fact, random));
    }

    private ContextMessage BuildMessage(
        MessageRole role,
        List<string> segments,
        IReadOnlyList<string> templates,
        Random random,
        int targetTokensPerMessage)
    {
        var desiredTokens = (int)Math.Round(targetTokensPerMessage * (MinVariance + (random.NextDouble() * (MaxVariance - MinVariance))));
        var text = NormalizeText(string.Join(" ", segments));
        var message = ContextMessage.FromText(role, text);

        while (this._tokenCounter.Count(message) < desiredTokens)
        {
            text = NormalizeText(text + " " + templates[random.Next(templates.Count)]);
            message = ContextMessage.FromText(role, text);

            if (this._tokenCounter.Count(message) >= desiredTokens)
            {
                break;
            }

            if (text.Length > desiredTokens * 16)
            {
                break;
            }
        }

        return message with { TokenCount = this._tokenCounter.Count(message) };
    }

    private static void AddNoiseSegment(List<string> target, IReadOnlyList<string> templates, Random random)
    {
        target.Add(templates[random.Next(templates.Count)]);

        if (random.NextDouble() < 0.35)
        {
            target.Add(templates[random.Next(templates.Count)]);
        }
    }

    private static IReadOnlyList<int> GetReinforcementTurns(PlantedFact fact, int turnCount)
    {
        var span = Math.Max(1, turnCount - fact.PlantedAtTurn - 1);
        var first = Math.Min(turnCount - 1, fact.PlantedAtTurn + Math.Max(1, span / 3));
        var second = Math.Min(turnCount - 1, fact.PlantedAtTurn + Math.Max(2, (span * 2) / 3));

        if (first == fact.PlantedAtTurn)
        {
            first = Math.Min(turnCount - 1, fact.PlantedAtTurn + 1);
        }

        if (second <= first)
        {
            second = Math.Min(turnCount - 1, first + 1);
        }

        if (second == fact.PlantedAtTurn)
        {
            second = Math.Min(turnCount - 1, fact.PlantedAtTurn + 2);
        }

        return [first, second];
    }

    private static void AddSegmentToDeterministicSpeaker(int turnIndex, List<string> userSegments, List<string> modelSegments, string text)
    {
        if (turnIndex % 2 == 0)
        {
            userSegments.Add(text);
            return;
        }

        modelSegments.Add(text);
    }

    private static string BuildAnchorSentence(PlantedFact fact, Random random)
    {
        string[] intros =
        [
            "One detail we should keep fixed is",
            "For reference,",
            "Before we move on, note that",
            "We already agreed that"
        ];

        return $"{intros[random.Next(intros.Length)]} {fact.Question.TrimEnd('?').ToLowerInvariant()} is {fact.GroundTruth}.";
    }

    private static string BuildReinforcedPrimarySentence(PlantedFact fact, Random random)
    {
        string[] intros =
        [
            "Let us lock this in early:",
            "Important context for later is that",
            "We should keep in mind that",
            "One thing we do not want to lose is that"
        ];

        return $"{intros[random.Next(intros.Length)]} {fact.Question.TrimEnd('?').ToLowerInvariant()} is {fact.GroundTruth}.";
    }

    private static string BuildReinforcementSentence(PlantedFact fact, Random random)
    {
        string[] templates =
        [
            $"That still lines up with earlier notes that {fact.GroundTruth} is answer to {ToClause(fact.Question)}.",
            $"Nothing has changed on that point: {fact.GroundTruth} remains answer to {ToClause(fact.Question)}.",
            $"We keep coming back to same detail, namely that {ToClause(fact.Question)} is {fact.GroundTruth}.",
            $"This only works if we remember that {fact.GroundTruth} is still correct for {ToClause(fact.Question)}."
        ];

        return templates[random.Next(templates.Length)];
    }

    private static string BuildSupersededInitialSentence(PlantedFact fact, Random random)
    {
        string[] intros =
        [
            "At the moment,",
            "Current working assumption is that",
            "For this draft,",
            "Right now,"
        ];

        return $"{intros[random.Next(intros.Length)]} {ToClause(fact.Question)} is {fact.OriginalValue}, though we may revise it after review.";
    }

    private static string BuildSupersededFinalSentence(PlantedFact fact, Random random)
    {
        string[] intros =
        [
            "Update from latest review:",
            "We should replace earlier value because",
            "Correction for planning notes:",
            "Latest confirmed value is different now:"
        ];

        return $"{intros[random.Next(intros.Length)]} {ToClause(fact.Question)} is {fact.GroundTruth}.";
    }

    private static string BuildRelationalSentence(PlantedFact fact, PlantedFact dependency, Random random)
    {
        string[] intros =
        [
            "To connect this with earlier setup,",
            "Building on prior context,",
            "Related to that earlier detail,",
            "Using same shared context,"
        ];

        return $"{intros[random.Next(intros.Length)]} {ToClause(fact.Question)} is {fact.GroundTruth}, and that depends on {dependency.GroundTruth} from {ToClause(dependency.Question)}.";
    }

    private static string BuildBuriedParagraph(PlantedFact fact, Random random)
    {
        string[] starts =
        [
            "Most of this thread has been about trade-offs, rough sequencing, and how much cleanup we can absorb without pushing visible work.",
            "There were several side conversations about tooling friction, uneven handoffs, and the usual question of whether we are optimizing the right bottleneck first.",
            "The discussion drifted through implementation detail, documentation ownership, and a short tangent about how prior estimates keep missing integration overhead.",
            "We spent more time than expected on surrounding process issues, especially where environment drift and review timing make routine work feel larger than it is."
        ];

        string[] middles =
        [
            $"In middle of that, someone confirmed that {ToClause(fact.Question)} is {fact.GroundTruth}, but conversation immediately moved back to rollout pacing and validation order.",
            $"One practical note slipped in that {fact.GroundTruth} is answer to {ToClause(fact.Question)}, although it barely changed the main conversation about dependencies and timing.",
            $"Buried between status updates was a reminder that {ToClause(fact.Question)} is {fact.GroundTruth}, then group returned to talking about test coverage and handoff risk.",
            $"Amid all the noise, we did state that {fact.GroundTruth} is correct for {ToClause(fact.Question)}, but it was not treated as main point of discussion."
        ];

        string[] ends =
        [
            "After that, the rest centered on keeping scope stable and avoiding late churn from loosely defined follow-up requests.",
            "The thread then wandered back into release criteria, owner alignment, and whether the current milestone still has enough schedule buffer.",
            "From there the room returned to dependency mapping, review timing, and how to keep the next batch of work legible to people joining midstream.",
            "The closing comments mostly focused on rollout risk, documentation debt, and how to prevent small ambiguities from turning into larger delays later."
        ];

        return $"{starts[random.Next(starts.Length)]} {middles[random.Next(middles.Length)]} {ends[random.Next(ends.Length)]}";
    }

    private static string BuildRecallProbe(IReadOnlyList<PlantedFact> facts)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Answer each question with ONLY the fact. No explanation.");

        foreach (var pair in facts.OrderBy(fact => fact.Id, StringComparer.Ordinal).Select((fact, index) => (fact, index)))
        {
            builder.Append("Q");
            builder.Append(pair.index + 1);
            builder.Append(": ");
            builder.AppendLine(pair.fact.Question);
        }

        return builder.ToString().TrimEnd();
    }

    private static string NormalizeText(string text)
    {
        return string.Join(" ", text.Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries));
    }

    private static string ToClause(string question)
    {
        return question.Trim().TrimEnd('?').ToLowerInvariant();
    }
}
