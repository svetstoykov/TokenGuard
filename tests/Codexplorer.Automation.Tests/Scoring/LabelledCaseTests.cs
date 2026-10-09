using System.Text.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Codexplorer.Automation.Scoring;
using FluentAssertions;

namespace Codexplorer.Automation.Tests.Scoring;

/// <summary>Verifies each independently reviewed scorer specimen.</summary>
public sealed class LabelledCaseTests
{
    /// <summary>Enumerates fixture display names independently of the working directory.</summary>
    /// <returns>One theory row per reviewed specimen.</returns>
    public static IEnumerable<object[]> Cases() => ReadCases().Select(item => new object[] { item.Name });

    /// <summary>Verifies the scorer matches a hand-labelled text verdict.</summary>
    /// <param name="name">The unique scenario name.</param>
    [Theory]
    [MemberData(nameof(Cases))]
    public void EvaluateCheck_MatchesReviewedLabel(string name)
    {
        var item = ReadCases().Single(item => item.Name == name);
        var text = item.Text ?? File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scoring", "Texts", item.TextFile!));
        var result = new AnswerScorer().EvaluateCheck(item.Check, text, item.Check.Artifact is null ? "noAnswer" : "artifactMissing");
        result.Passed.Should().Be(item.Expected.Passed, $"{name}: {item.Why}; actual reason {result.Reason}");
        result.Reason.Should().Be(item.Expected.Reason, $"{name}: {item.Why}");
    }

    private static IReadOnlyList<LabelledScorerCase> ReadCases()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scoring", "ScorerCases.json"));
        var cases = JsonSerializer.Deserialize<LabelledScorerCase[]>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        })!;
        cases.Should().NotBeEmpty();
        cases.Select(item => item.Name).Should().OnlyHaveUniqueItems();
        foreach (var item in cases)
        {
            item.Name.Should().NotBeNullOrWhiteSpace();
            item.Why.Should().NotBeNullOrWhiteSpace();
            item.Source.Should().NotBeNullOrWhiteSpace();
            (item.Text is null ^ item.TextFile is null).Should().BeTrue("exactly one text source must be supplied");
            if (item.TextFile is { } file)
            {
                file.Should().NotBeNullOrWhiteSpace();
                Path.GetFileName(file).Should().Be(file, "fixture files remain below Texts");
                var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Scoring", "Texts", file))));
                hash.Should().BeEquivalentTo(item.Source.Split("sha256:")[1], "recorded text must retain its reviewed bytes");
            }
        }
        return cases;
    }
}
