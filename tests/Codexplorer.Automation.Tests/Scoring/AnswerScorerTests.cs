using Codexplorer.Automation.Configuration;
using Codexplorer.Automation.Scoring;
using Codexplorer.Measurements;
using FluentAssertions;

namespace Codexplorer.Automation.Tests.Scoring;

/// <summary>Verifies check precedence and target availability.</summary>
public sealed class AnswerScorerTests
{
    /// <summary>Verifies positive matching before forbidden-value guards.</summary>
    /// <param name="text">The answer.</param>
    /// <param name="reason">The expected reason.</param>
    [Theory]
    [InlineData(null, "noAnswer")]
    [InlineData("  ", "noAnswer")]
    [InlineData("wrong", "notFound")]
    [InlineData("right wrong", "forbiddenValuePresent")]
    [InlineData("alternative", null)]
    [InlineData("**right**", null)]
    public void Check_PrecedenceIsDeterministic(string? text, string? reason)
    {
        var check = new AutomationCheckDefinition { Id = "fact", Kind = "contains", AnyOf = ["right", "alternative"], NoneOf = ["wrong"] };
        var result = new AnswerScorer().EvaluateCheck(check, text, "noAnswer");
        result.Reason.Should().Be(reason);
        result.Passed.Should().Be(reason is null);
    }

    /// <summary>Verifies regexes see cleaned text, while their own syntax is preserved.</summary>
    [Fact]
    public void Pattern_MatchesCleanedPathsAndAppliesGuard()
    {
        var check = new AutomationCheckDefinition { Id = "path", Kind = "matches", Pattern = @"src/main\.go.*30", NoneOf = ["wrong"] };
        var scorer = new AnswerScorer();
        scorer.EvaluateCheck(check, "`src\\main.go`\n**30**", "noAnswer").Passed.Should().BeTrue();
        scorer.EvaluateCheck(check, "src/main.go 30 wrong", "noAnswer").Reason.Should().Be("forbiddenValuePresent");
    }

    /// <summary>Verifies empty artifacts are available and checks retain their order.</summary>
    [Fact]
    public void Score_DistinguishesMissingAndEmptyArtifacts()
    {
        AutomationCheckDefinition Check(string id, string? artifact, string kind = "contains") =>
            new()
            {
                Id = id, Artifact = artifact, Kind = kind, AnyOf = kind == "contains" ? ["fact"] : null,
                Pattern = kind == "matches" ? "^$" : null,
            };
        var result = new AnswerScorer().Score([Check("a", null), Check("b", "empty"), Check("c", "missing"), Check("d", "empty", "matches")],
            null, new ScoringInput { FinalAnswer = " ", ArtifactTexts = new Dictionary<string, string> { ["empty"] = "" }, Measurements = new() });
        result.Checks.Select(check => check.Id).Should().Equal("a", "b", "c", "d");
        result.Checks.Select(check => check.Reason).Should().Equal("noAnswer", "notFound", "artifactMissing", null);
        result.Probe.Should().BeNull();
        new AnswerScorer().Score([], null, new ScoringInput { ArtifactTexts = new Dictionary<string, string>(), Measurements = new() })
            .Checks.Should().BeEmpty();
    }

    /// <summary>Verifies probe precedence and informational code presence.</summary>
    /// <param name="answer">The available answer.</param>
    /// <param name="repeated">Whether the code was repeated early.</param>
    /// <param name="opening">Whether the opening instruction survived.</param>
    /// <param name="count">The masked counter.</param>
    /// <param name="status">The expected status.</param>
    /// <param name="reason">The expected reason.</param>
    /// <param name="present">The expected code presence.</param>
    [Theory]
    [InlineData(null, true, true, 0, "invalid", "noAnswer", false)]
    [InlineData("ABC123", true, true, 0, "invalid", "canaryRepeated", true)]
    [InlineData("ABC123", false, true, 1, "invalid", "instructionNotCompacted", true)]
    [InlineData("ABC123", false, false, 0, "invalid", "requiredKindAbsent", true)]
    [InlineData("a**bc**123", false, false, 1, "passed", null, true)]
    [InlineData("xABC123", false, false, 1, "failed", "canaryMissing", false)]
    public void Probe_AppliesOrderedPrecedence(string? answer, bool repeated, bool opening, long count,
        string status, string? reason, bool present)
    {
        var result = new AnswerScorer().Score([], new AutomationProbeDefinition { Canary = "ABC123", Requires = "masked" },
            new ScoringInput
            {
                FinalAnswer = answer, CanaryRepeated = repeated, ArtifactTexts = new Dictionary<string, string>(),
                Measurements = new SessionMeasurements
                {
                    MessagesMasked = count,
                    PrepareRecords = [new PrepareMeasurement { Index = 1, Status = "completed", OpeningMessagePresent = opening }]
                }
            });
        result.Probe!.Status.Should().Be(status);
        result.Probe.Reason.Should().Be(reason);
        result.Probe.CanaryPresent.Should().Be(present);
    }
}
