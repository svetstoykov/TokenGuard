using Codexplorer.Automation.Configuration;
using Codexplorer.Automation.Scoring;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Codexplorer.Automation.Tests.Configuration;

/// <summary>Verifies the long-session corpus manifests and the facts their checks accept.</summary>
public sealed class LongSessionCorpusTests
{
    private static readonly string[] Manifests = ["long-sessions-100k.json", "long-sessions-180k.json", "long-sessions-280k.json"];

    /// <summary>Verifies each tier holds two workloads.</summary>
    /// <param name="manifest">The tier manifest file name.</param>
    [Theory]
    [InlineData("long-sessions-100k.json")]
    [InlineData("long-sessions-180k.json")]
    [InlineData("long-sessions-280k.json")]
    public void LoadTasks_LongSessionTier_LoadsTwoTasks(string manifest)
    {
        var tasks = LoadTasks(manifest);

        tasks.Should().HaveCount(2);
    }

    /// <summary>Verifies each workload declares enough facts to spread across a long session.</summary>
    /// <param name="manifest">The tier manifest file name.</param>
    [Theory]
    [InlineData("long-sessions-100k.json")]
    [InlineData("long-sessions-180k.json")]
    [InlineData("long-sessions-280k.json")]
    public void LoadTasks_LongSessionTier_DeclaresAtLeastSixChecksPerTask(string manifest)
    {
        var tasks = LoadTasks(manifest);

        tasks.Should().OnlyContain(task => task.Checks != null && task.Checks.Count >= 6);
    }

    /// <summary>Verifies each workload checks one fact in the final answer and the rest in the artifact.</summary>
    /// <param name="manifest">The tier manifest file name.</param>
    [Theory]
    [InlineData("long-sessions-100k.json")]
    [InlineData("long-sessions-180k.json")]
    [InlineData("long-sessions-280k.json")]
    public void LoadTasks_LongSessionTier_ChecksOneFactInFinalAnswer(string manifest)
    {
        var tasks = LoadTasks(manifest);

        tasks.Should().OnlyContain(task => task.Checks!.Count(check => check.Artifact == null) == 1);
    }

    /// <summary>Verifies each tier carries one retention probe.</summary>
    /// <param name="manifest">The tier manifest file name.</param>
    [Theory]
    [InlineData("long-sessions-100k.json")]
    [InlineData("long-sessions-180k.json")]
    [InlineData("long-sessions-280k.json")]
    public void LoadTasks_LongSessionTier_DeclaresOneProbe(string manifest)
    {
        var tasks = LoadTasks(manifest);

        tasks.Count(task => task.Probe is not null).Should().Be(1);
    }

    /// <summary>Verifies the tiers reuse the verifiable corpus checkout of each repository.</summary>
    [Fact]
    public void LoadTasks_LongSessionTiers_PinRepositoriesToVerifiableCorpusCommits()
    {
        var pins = LoadTasks("verifiable-corpus.json").Select(task => (task.RepositoryUrl, task.RepositoryCommit)).Distinct().ToArray();

        var used = Manifests.SelectMany(LoadTasks).Select(task => (task.RepositoryUrl, task.RepositoryCommit)).Distinct().ToArray();

        used.Should().BeSubsetOf(pins);
    }

    /// <summary>Verifies a task identifier names one workload across all tiers.</summary>
    [Fact]
    public void LoadTasks_LongSessionTiers_UseDistinctTaskIds()
    {
        var tasks = Manifests.SelectMany(LoadTasks).ToArray();

        tasks.Select(task => task.TaskId).Should().OnlyHaveUniqueItems();
    }

    /// <summary>Enumerates every check with the value read from the pinned source and a different value.</summary>
    /// <returns>One scoring scenario per check, text format, and expected verdict.</returns>
    public static IEnumerable<object[]> FactCases()
    {
        (string Manifest, string TaskId, string CheckId, string Correct, string Wrong)[] facts =
        [
            ("long-sessions-100k.json", "bat-output-audit", "gutter-color-answer", "The gutter falls back to colour 238.", "The gutter falls back to colour 2380."),
            ("long-sessions-100k.json", "bat-output-audit", "gutter-color", "gutter-color: 238", "gutter-color: 236"),
            ("long-sessions-100k.json", "bat-output-audit", "no-init-below", "no-init-below: 530", "no-init-below: 558"),
            ("long-sessions-100k.json", "bat-output-audit", "no-init-below-windows", "no-init-below-windows: 558", "no-init-below-windows: 530"),
            ("long-sessions-100k.json", "bat-output-audit", "system-config-prefix", "system-config-prefix: /etc", "system-config-prefix: /etc/bat"),
            ("long-sessions-100k.json", "bat-output-audit", "fallback-language-width", "fallback-language-width: 32", "fallback-language-width: 320"),
            ("long-sessions-100k.json", "bat-output-audit", "metadata-file", "metadata-file: metadata.yaml", "metadata-file: metadata.yml"),
            ("long-sessions-100k.json", "rg-search-defaults", "buffer-constant", "The capacity is DEFAULT_BUFFER_CAPACITY.", "The capacity is DEFAULT_BUFFER_CAPACITY_KB."),
            ("long-sessions-100k.json", "rg-search-defaults", "thread-cap", "thread-cap: 12", "thread-cap: 16"),
            ("long-sessions-100k.json", "rg-search-defaults", "mmap-path-ceiling", "mmap-path-ceiling: 10", "mmap-path-ceiling: 100"),
            ("long-sessions-100k.json", "rg-search-defaults", "line-buffer-bytes", "line-buffer-bytes: 65536", "line-buffer-bytes: 8192"),
            ("long-sessions-100k.json", "rg-search-defaults", "decode-buffer-bytes", "decode-buffer-bytes: 8192", "decode-buffer-bytes: 65536"),
            ("long-sessions-100k.json", "rg-search-defaults", "jit-stack-bytes", "jit-stack-bytes: 10485760", "jit-stack-bytes: 1048576"),
            ("long-sessions-180k.json", "rg-ignore-and-globs", "excludes-default-answer", "The fallback comes from excludes_file_default.", "The fallback comes from excludes_file_default_path."),
            ("long-sessions-180k.json", "rg-ignore-and-globs", "walker-fallback-threads", "walker-fallback-threads: 2", "walker-fallback-threads: 12"),
            ("long-sessions-180k.json", "rg-ignore-and-globs", "ts-glob-count", "ts-glob-count: 4", "ts-glob-count: 2"),
            ("long-sessions-180k.json", "rg-ignore-and-globs", "py-second-glob", "py-second-glob: *.pyi", "py-second-glob: *.py"),
            ("long-sessions-180k.json", "rg-ignore-and-globs", "glob-strategy-count", "glob-strategy-count: 7", "glob-strategy-count: 6"),
            ("long-sessions-180k.json", "rg-ignore-and-globs", "global-excludes-function", "global-excludes-function: gitconfig_excludes_path", "global-excludes-function: excludes_file_default"),
            ("long-sessions-180k.json", "rg-ignore-and-globs", "excludes-default-function", "excludes-default-function: excludes_file_default", "excludes-default-function: gitconfig_excludes_path"),
            ("long-sessions-180k.json", "rg-ignore-and-globs", "own-ignore-file", "own-ignore-file: .rgignore", "own-ignore-file: .ignore"),
            ("long-sessions-180k.json", "gh-list-defaults", "run-delete-constant", "The lookup is bounded by defaultRunGetLimit.", "The lookup is bounded by defaultLimit."),
            ("long-sessions-180k.json", "gh-list-defaults", "run-list-limit", "run-list-limit: 20", "run-list-limit: 30"),
            ("long-sessions-180k.json", "gh-list-defaults", "workflow-list-limit", "workflow-list-limit: 50", "workflow-list-limit: 30"),
            ("long-sessions-180k.json", "gh-list-defaults", "gist-list-limit", "gist-list-limit: 10", "gist-list-limit: 100"),
            ("long-sessions-180k.json", "gh-list-defaults", "run-delete-lookup", "run-delete-lookup: 10", "run-delete-lookup: 20"),
            ("long-sessions-180k.json", "gh-list-defaults", "run-watch-interval-seconds", "run-watch-interval-seconds: 3", "run-watch-interval-seconds: 10"),
            ("long-sessions-180k.json", "gh-list-defaults", "pr-checks-interval-seconds", "pr-checks-interval-seconds: 10", "pr-checks-interval-seconds: 3"),
            ("long-sessions-180k.json", "gh-list-defaults", "project-limit-max", "project-limit-max: 100", "project-limit-max: 30"),
            ("long-sessions-280k.json", "rg-flags-and-printers", "look-ahead-constant", "The limit is MAX_LOOK_AHEAD.", "The limit is MAX_LOOK_AHEAD_BYTES."),
            ("long-sessions-280k.json", "rg-flags-and-printers", "look-ahead-bytes", "look-ahead-bytes: 128", "look-ahead-bytes: 1280"),
            ("long-sessions-280k.json", "rg-flags-and-printers", "error-exit-code", "error-exit-code: 2", "error-exit-code: 1"),
            ("long-sessions-280k.json", "rg-flags-and-printers", "no-match-exit-code", "no-match-exit-code: 1", "no-match-exit-code: 2"),
            ("long-sessions-280k.json", "rg-flags-and-printers", "default-path-color", "default-path-color: magenta", "default-path-color: cyan"),
            ("long-sessions-280k.json", "rg-flags-and-printers", "default-path-color-windows", "default-path-color-windows: cyan", "default-path-color-windows: magenta"),
            ("long-sessions-280k.json", "rg-flags-and-printers", "default-line-color", "default-line-color: green", "default-line-color: red"),
            ("long-sessions-280k.json", "rg-flags-and-printers", "summary-kind-count", "summary-kind-count: 5", "summary-kind-count: 6"),
            ("long-sessions-280k.json", "rg-flags-and-printers", "kitty-format", "kitty-format: file://{host}{path}#{line}", "kitty-format: file://{host}{path}"),
            ("long-sessions-280k.json", "rg-flags-and-printers", "omitted-match-text", "omitted-match-text: [Omitted long matching line]", "omitted-match-text: [Omitted long context line]"),
            ("long-sessions-280k.json", "gh-codespaces-timing", "display-name-constant", "The limit is displayNameMaxLength.", "The limit is displayNameMax."),
            ("long-sessions-280k.json", "gh-codespaces-timing", "permissions-polling-interval-seconds", "permissions-polling-interval-seconds: 5", "permissions-polling-interval-seconds: 60"),
            ("long-sessions-280k.json", "gh-codespaces-timing", "permissions-polling-timeout-seconds", "permissions-polling-timeout-seconds: 60", "permissions-polling-timeout-seconds: 5"),
            ("long-sessions-280k.json", "gh-codespaces-timing", "display-name-max", "display-name-max: 48", "display-name-max: 64"),
            ("long-sessions-280k.json", "gh-codespaces-timing", "rpc-connection-timeout-seconds", "rpc-connection-timeout-seconds: 5", "rpc-connection-timeout-seconds: 30"),
            ("long-sessions-280k.json", "gh-codespaces-timing", "rpc-request-timeout-seconds", "rpc-request-timeout-seconds: 30", "rpc-request-timeout-seconds: 5"),
            ("long-sessions-280k.json", "gh-codespaces-timing", "heartbeat-interval-seconds", "heartbeat-interval-seconds: 60", "heartbeat-interval-seconds: 30"),
            ("long-sessions-280k.json", "gh-codespaces-timing", "backoff-max-interval-seconds", "backoff-max-interval-seconds: 10", "backoff-max-interval-seconds: 1"),
            ("long-sessions-280k.json", "gh-codespaces-timing", "backoff-max-elapsed-seconds", "backoff-max-elapsed-seconds: 300", "backoff-max-elapsed-seconds: 120"),
            ("long-sessions-280k.json", "gh-codespaces-timing", "list-page-size", "list-page-size: 100", "list-page-size: 30"),
        ];
        (string Format, bool KeepsVerdict)[] formats = [("{0}", true), ("- {0}", true), ("{0}\nSource evidence follows.", true)];
        foreach (var fact in facts)
        {
            foreach (var format in formats)
            {
                yield return [fact.Manifest, fact.TaskId, fact.CheckId, string.Format(format.Format, fact.Correct), true];
                yield return [fact.Manifest, fact.TaskId, fact.CheckId, string.Format(format.Format, fact.Wrong), false];
            }
        }
    }

    /// <summary>Verifies each check accepts the value in the pinned source and rejects a different one.</summary>
    /// <param name="manifest">The tier manifest file name.</param>
    /// <param name="taskId">The workload identifier.</param>
    /// <param name="checkId">The check identifier.</param>
    /// <param name="text">The answer or artifact text to score. Cannot be <see langword="null" />.</param>
    /// <param name="passed">The independently expected verdict.</param>
    [Theory]
    [MemberData(nameof(FactCases))]
    public void EvaluateCheck_LongSessionFact_MatchesExpectedValue(string manifest, string taskId, string checkId, string text, bool passed)
    {
        var check = LoadTasks(manifest).Single(task => task.TaskId == taskId).Checks!.Single(check => check.Id == checkId);

        var result = new AnswerScorer().EvaluateCheck(check, text, "noAnswer");

        result.Passed.Should().Be(passed);
    }

    /// <summary>Verifies keyed facts survive the emphasis and code formatting models add to artifact lines.</summary>
    /// <param name="text">The artifact line to score. Cannot be <see langword="null" />.</param>
    [Theory]
    [InlineData("**gutter-color:** 238")]
    [InlineData("`gutter-color`: `238`")]
    [InlineData("- **gutter-color**: 238.")]
    [InlineData("gutter-color = 238 (src/printer.rs)")]
    public void EvaluateCheck_KeyedFactWithMarkdownFormatting_Passes(string text)
    {
        var check = LoadTasks("long-sessions-100k.json").Single(task => task.TaskId == "bat-output-audit").Checks!
            .Single(check => check.Id == "gutter-color");

        var result = new AnswerScorer().EvaluateCheck(check, text, "noAnswer");

        result.Passed.Should().BeTrue();
    }

    private static IReadOnlyList<AutomationTaskDefinition> LoadTasks(string manifest)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TokenGuard.sln")))
        {
            directory = directory.Parent;
        }
        directory.Should().NotBeNull("the test output must be inside the repository");
        var path = Path.Combine(directory!.FullName, "samples", "Codexplorer.Automation", "src", "tasks", manifest);
        var loader = new AutomationTaskManifestLoader(Options.Create(new CodexplorerAutomationOptions { ManifestPath = path }),
            NullLogger<AutomationTaskManifestLoader>.Instance, new AnswerScorer());
        return loader.LoadTasks();
    }
}
