using System.Text.Json;
using Codexplorer.Automation.AgentComparison;
using Codexplorer.Automation.Configuration;
using Codexplorer.Automation.Scoring;
using FluentAssertions;

namespace Codexplorer.Automation.Tests.AgentComparison;

/// <summary>Verifies unchanged scorer outcomes and separate evidence populations.</summary>
public sealed class ComparisonScoringTests
{
    /// <summary>Runs the existing shared scorer fixtures through both comparison populations.</summary>
    [Fact]
    public void Score_SharedFixtures_PreservesOriginalCheckVerdicts()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var fixtures = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scoring", "ScorerCases.json")));
            foreach (var fixture in fixtures.RootElement.EnumerateArray())
            {
                var check = fixture.GetProperty("check").Deserialize<AutomationCheckDefinition>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
                var text = fixture.TryGetProperty("textFile", out var textFile)
                    ? File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scoring", "Texts", textFile.GetString()!))
                    : fixture.GetProperty("text").ValueKind == JsonValueKind.Null ? null : fixture.GetProperty("text").GetString();
                var legacy = new AnswerScorer().EvaluateCheck(check, text, "noAnswer");
                var task = new AutomationTaskDefinition { TaskId = "fixture", Checks = [check with { Artifact = null }] };
                var session = new ComparisonSessionPlan("fixture", "synthetic", 1, "opencode", task, "prompt", "hash", "hash", 5, 1, null);
                IComparisonScoring scorer = new ComparisonScoring(new AnswerScorer());
                var answer = scorer.Score(session, root, text);
                answer.FinalAnswer.Single().Should().BeEquivalentTo(legacy);
                answer.Evidence.Should().BeEmpty();
                var file = Path.Combine(root, "evidence.md");
                if (text is not null)
                    File.WriteAllText(file, text);
                else if (File.Exists(file))
                    File.Delete(file);
                task = task with { Checks = [check with { Artifact = "evidence.md" }] };
                var evidence = scorer.Score(session with { Task = task }, root, "irrelevant");
                evidence.Evidence.Single().Should().BeEquivalentTo(new AnswerScorer().EvaluateCheck(check, text, "artifactMissing"));
                evidence.FinalAnswer.Should().BeEmpty();
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Rejects probe-bearing tasks instead of manufacturing runtime telemetry.</summary>
    [Fact]
    public void Score_ProbeBearingTask_IsRejected()
    {
        var task = new AutomationTaskDefinition { Probe = new AutomationProbeDefinition { Canary = "probe" } };
        var session = new ComparisonSessionPlan("fixture", "synthetic", 1, "opencode", task, "prompt", "hash", "hash", 5, 1, null);
        IComparisonScoring scorer = new ComparisonScoring(new AnswerScorer());
        var action = () => scorer.Score(session, Path.GetTempPath(), "answer");
        action.Should().Throw<InvalidDataException>().WithMessage("*probe*");
    }
}
