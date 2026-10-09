using System.Text.RegularExpressions;
using Codexplorer.Automation.Configuration;
using Codexplorer.Automation.Scoring;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Codexplorer.Automation.Tests.Configuration;

/// <summary>Verifies the shipped corpus can run with pinned, checkable tasks and retention probes.</summary>
public sealed class VerifiableCorpusTests
{
    /// <summary>Verifies the validated corpus contains enough workloads.</summary>
    [Fact]
    public void LoadTasks_VerifiableCorpus_LoadsAtLeastTenTasks()
    {
        var tasks = LoadTasks();

        tasks.Should().HaveCountGreaterThanOrEqualTo(10);
    }

    /// <summary>Verifies every workload has a pinned repository.</summary>
    [Fact]
    public void LoadTasks_VerifiableCorpus_PinsEveryTask()
    {
        var tasks = LoadTasks();

        tasks.Should().OnlyContain(task => !string.IsNullOrWhiteSpace(task.RepositoryCommit));
    }

    /// <summary>Verifies every workload declares enough deliverable checks.</summary>
    [Fact]
    public void LoadTasks_VerifiableCorpus_DeclaresAtLeastThreeChecksPerTask()
    {
        var tasks = LoadTasks();

        tasks.Should().OnlyContain(task => task.Checks != null && task.Checks.Count >= 3);
    }

    /// <summary>Verifies each workload size has enough coverage.</summary>
    /// <param name="sizeName">The workload size to count. Cannot be <see langword="null" />.</param>
    [Theory]
    [InlineData("Small")]
    [InlineData("Medium")]
    [InlineData("Large")]
    public void LoadTasks_VerifiableCorpus_LoadsAtLeastThreeTasksOfEachSize(string sizeName)
    {
        var tasks = LoadTasks();
        var size = Enum.Parse<RunnerTaskSize>(sizeName);

        tasks.Count(task => task.TaskSize == size).Should().BeGreaterThanOrEqualTo(3);
    }

    /// <summary>Verifies tasks share one pinned checkout per repository.</summary>
    [Fact]
    public void LoadTasks_VerifiableCorpus_ReusesOneCommitPerRepository()
    {
        var tasks = LoadTasks();

        foreach (var repository in tasks.GroupBy(task => task.RepositoryUrl))
        {
            repository.Select(task => task.RepositoryCommit).Distinct().Should().ContainSingle("tasks reuse one pinned checkout per repository");
        }
    }

    /// <summary>Verifies enough workloads declare retention probes.</summary>
    [Fact]
    public void LoadTasks_VerifiableCorpus_DeclaresAtLeastFourProbes()
    {
        var probes = LoadTasks().Where(task => task.Probe is not null).ToArray();

        probes.Should().HaveCountGreaterThanOrEqualTo(4);
    }

    /// <summary>Verifies retention probes use workloads large enough to exercise compaction.</summary>
    [Fact]
    public void LoadTasks_VerifiableCorpus_UsesMediumOrLargeProbeWorkloads()
    {
        var probes = LoadTasks().Where(task => task.Probe is not null).ToArray();

        probes.Should().OnlyContain(task => task.TaskSize != RunnerTaskSize.Small);
    }

    /// <summary>Verifies an emergency probe requests several substantial source ranges.</summary>
    [Fact]
    public void LoadTasks_VerifiableCorpus_DeclaresEmergencyProbeWithSeveralLargeRanges()
    {
        var emergency = LoadTasks().Where(task => task.Probe?.Requires == "dropped").ToArray();

        emergency.Should().NotBeEmpty();
        foreach (var task in emergency)
        {
            var ranges = Regex.Matches(task.InitialPrompt!, @"[\w./]+ lines (?<start>\d+)-(?<end>\d+)");
            ranges.Count(match => int.Parse(match.Groups["end"].Value) - int.Parse(match.Groups["start"].Value) + 1 >= 200)
                .Should().BeGreaterThanOrEqualTo(3);
        }
    }

    /// <summary>Verifies the final answer carries a concrete fact for each workload.</summary>
    /// <param name="taskId">The workload identifier.</param>
    /// <param name="checkId">The fact required in the final answer.</param>
    [Theory]
    [InlineData("bat-theme-defaults", "light-theme")]
    [InlineData("bat-pager-precedence", "environment-variant")]
    [InlineData("bat-asset-loading", "integrated-theme-helper")]
    [InlineData("rg-thread-selection", "automatic-cap")]
    [InlineData("rg-encoding-disable", "encoding-regression")]
    [InlineData("rg-preprocessor-retention", "dispatch-predicate")]
    [InlineData("rg-binary-retention", "quit-constructor")]
    [InlineData("gh-api-emergency-retention", "end-cursor-helper")]
    [InlineData("gh-clone-retention", "base-remote-method")]
    [InlineData("gh-list-pagination", "search-cap-field")]
    public void LoadTasks_VerifiableCorpus_ChecksConcreteFactInFinalAnswer(string taskId, string checkId)
    {
        var task = LoadTasks().Single(task => task.TaskId == taskId);

        task.Checks!.Single(check => check.Id == checkId).Artifact.Should().BeNull();
    }

    /// <summary>Enumerates correct numeric facts with ordinary answer punctuation and incorrect adjacent values.</summary>
    /// <returns>One scoring scenario per numeric fact and text format.</returns>
    public static IEnumerable<object[]> NumericCases()
    {
        (string TaskId, string CheckId, string Label, string Value)[] facts =
        [
            ("rg-thread-selection", "automatic-cap", "automatic-thread-cap", "12"),
            ("rg-thread-selection", "sorted-count", "sorted-thread-count", "1"),
            ("gh-api-emergency-retention", "rest-page-size", "rest-page-size", "100"),
            ("gh-list-pagination", "default-list-limit", "default-list-limit", "30"),
            ("gh-list-pagination", "search-result-ceiling", "search-result-ceiling", "1000"),
        ];
        (string Format, bool Passed)[] formats =
        [
            ("{0}", true), ("{0}.", true), ("'{0}'", true), ("\"{0}\"", true), ("({0})", true), ("[{0}]", true),
            ("**{0}**", true), ("`{0}`", true), ("{0}\nSource evidence follows.", true), ("{0}, verified", true),
            ("{0}; verified", true), ("{0}. Verified.", true),
            ("{0}0", false), ("{0}.5", false), ("{0}-16", false), ("{0}a", false),
        ];
        foreach (var fact in facts)
        {
            foreach (var format in formats)
            {
                yield return [fact.TaskId, fact.CheckId, string.Format(format.Format, $"{fact.Label}: {fact.Value}"), format.Passed];
            }
        }
    }

    /// <summary>Verifies numeric checks accept punctuation while rejecting different values.</summary>
    /// <param name="taskId">The workload identifier.</param>
    /// <param name="checkId">The numeric check identifier.</param>
    /// <param name="text">The answer text to score. Cannot be <see langword="null" />.</param>
    /// <param name="passed">The independently expected verdict.</param>
    [Theory]
    [MemberData(nameof(NumericCases))]
    public void EvaluateCheck_NumericFact_MatchesExpectedValue(string taskId, string checkId, string text, bool passed)
    {
        var check = LoadTasks().Single(task => task.TaskId == taskId).Checks!.Single(check => check.Id == checkId);

        var result = new AnswerScorer().EvaluateCheck(check, text, "noAnswer");

        result.Passed.Should().Be(passed);
    }

    /// <summary>Verifies symbol checks distinguish the intended declaration from nearby names and unrelated members.</summary>
    /// <param name="taskId">The workload identifier.</param>
    /// <param name="checkId">The symbol check identifier.</param>
    /// <param name="text">The symbol text to score. Cannot be <see langword="null" />.</param>
    /// <param name="passed">The independently expected verdict.</param>
    [Theory]
    [InlineData("bat-pager-precedence", "selection-function", "`get_pager`", true)]
    [InlineData("bat-pager-precedence", "selection-function", "get_pager_executable", false)]
    [InlineData("bat-theme-defaults", "default-function", "theme::default_theme", true)]
    [InlineData("bat-theme-defaults", "default-function", "HighlightingAssets::default_theme", false)]
    [InlineData("rg-preprocessor-retention", "glob-builder", "flags::hiargs::preprocessor_globs", true)]
    [InlineData("rg-preprocessor-retention", "glob-builder", "rg::flags::hiargs::preprocessor_globs", true)]
    [InlineData("rg-preprocessor-retention", "glob-builder", "rg::flags::hiargs::preprocessor_globs_extra", false)]
    [InlineData("rg-preprocessor-retention", "glob-builder", "SearchWorkerBuilder::preprocessor_globs", false)]
    [InlineData("rg-preprocessor-retention", "glob-builder", "preprocessor_globs: Override", false)]
    public void EvaluateCheck_SymbolFact_MatchesIntendedDeclaration(string taskId, string checkId, string text, bool passed)
    {
        var check = LoadTasks().Single(task => task.TaskId == taskId).Checks!.Single(check => check.Id == checkId);

        var result = new AnswerScorer().EvaluateCheck(check, text, "noAnswer");

        result.Passed.Should().Be(passed);
    }

    private static IReadOnlyList<AutomationTaskDefinition> LoadTasks()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TokenGuard.sln")))
        {
            directory = directory.Parent;
        }
        directory.Should().NotBeNull("the test output must be inside the repository");
        var path = Path.Combine(directory!.FullName, "samples", "Codexplorer.Automation", "src", "tasks", "verifiable-corpus.json");
        var loader = new AutomationTaskManifestLoader(Options.Create(new CodexplorerAutomationOptions { ManifestPath = path }),
            NullLogger<AutomationTaskManifestLoader>.Instance, new AnswerScorer());
        return loader.LoadTasks();
    }
}
