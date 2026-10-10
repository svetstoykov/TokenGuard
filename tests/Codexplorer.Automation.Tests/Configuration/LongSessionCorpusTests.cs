using Codexplorer.Automation.Configuration;
using Codexplorer.Automation.Scoring;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Codexplorer.Automation.Tests.Configuration;

/// <summary>Verifies the long-session corpus manifests and the facts their checks accept.</summary>
public sealed class LongSessionCorpusTests
{
    private static readonly string[] Manifests = ["long-sessions-100k.json", "long-sessions-200k.json", "long-sessions-300k.json"];

    /// <summary>Verifies each tier holds two workloads.</summary>
    /// <param name="manifest">The tier manifest file name.</param>
    [Theory]
    [InlineData("long-sessions-100k.json")]
    [InlineData("long-sessions-200k.json")]
    [InlineData("long-sessions-300k.json")]
    public void LoadTasks_LongSessionTier_LoadsTwoTasks(string manifest)
    {
        var tasks = LoadTasks(manifest);

        tasks.Should().HaveCount(2);
    }

    /// <summary>Verifies each workload declares enough facts to spread across a long session.</summary>
    /// <param name="manifest">The tier manifest file name.</param>
    [Theory]
    [InlineData("long-sessions-100k.json")]
    [InlineData("long-sessions-200k.json")]
    [InlineData("long-sessions-300k.json")]
    public void LoadTasks_LongSessionTier_DeclaresAtLeastSixChecksPerTask(string manifest)
    {
        var tasks = LoadTasks(manifest);

        tasks.Should().OnlyContain(task => task.Checks != null && task.Checks.Count >= 6);
    }

    /// <summary>Verifies each workload checks one fact in the final answer and the rest in the artifact.</summary>
    /// <param name="manifest">The tier manifest file name.</param>
    [Theory]
    [InlineData("long-sessions-100k.json")]
    [InlineData("long-sessions-200k.json")]
    [InlineData("long-sessions-300k.json")]
    public void LoadTasks_LongSessionTier_ChecksOneFactInFinalAnswer(string manifest)
    {
        var tasks = LoadTasks(manifest);

        tasks.Should().OnlyContain(task => task.Checks!.Count(check => check.Artifact == null) == 1);
    }

    /// <summary>Verifies each workload checks a value that holds only after its edits.</summary>
    /// <param name="manifest">The tier manifest file name.</param>
    [Theory]
    [InlineData("long-sessions-100k.json")]
    [InlineData("long-sessions-200k.json")]
    [InlineData("long-sessions-300k.json")]
    public void LoadTasks_LongSessionTier_ChecksPostEditFactInArtifact(string manifest)
    {
        var tasks = LoadTasks(manifest);

        tasks.Should().OnlyContain(task => task.Checks!.Any(check => check.Artifact != null && check.Id!.EndsWith("-after")));
    }

    /// <summary>Verifies the tiers pin each repository to one commit.</summary>
    [Fact]
    public void LoadTasks_LongSessionTiers_PinEachRepositoryToOneCommit()
    {
        (string? RepositoryUrl, string? RepositoryCommit)[] pins =
        [
            ("https://github.com/gohugoio/hugo", "0732eadd5aead94d4bd4674d87e9bfc79aaf10a7"),
            ("https://github.com/django/django", "dab0a5c47f159eb58472110dcbc10bd4a9b01adb"),
            ("https://github.com/jellyfin/jellyfin", "a45d1415d4da7c9b69d6e7a6895fc63d2d68e8f1"),
            ("https://github.com/redis/redis", "3c7e951c3dc13fdeb905092e4a921ca7823e6faa"),
        ];

        var used = Manifests.SelectMany(LoadTasks).Select(task => (task.RepositoryUrl, task.RepositoryCommit)).Distinct().ToArray();

        used.Should().HaveCount(pins.Length).And.BeSubsetOf(pins);
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
            ("long-sessions-100k.json", "hugo-assembly-thresholds", "collect-log-file-step-after-answer", "collect-log-file-step-after: 200", "collect-log-file-step-after: 1000"),
            ("long-sessions-100k.json", "hugo-assembly-thresholds", "collect-log-seconds", "collect-log-seconds: 3", "collect-log-seconds: 30"),
            ("long-sessions-100k.json", "hugo-assembly-thresholds", "collect-log-file-step", "collect-log-file-step: 1000", "collect-log-file-step: 200"),
            ("long-sessions-100k.json", "hugo-assembly-thresholds", "eviction-ceiling", "eviction-ceiling: 200", "eviction-ceiling: 1000"),
            ("long-sessions-100k.json", "hugo-assembly-thresholds", "resources-cache-weight", "resources-cache-weight: 60", "resources-cache-weight: 70"),
            ("long-sessions-100k.json", "hugo-assembly-thresholds", "rendered-content-cache-weight", "rendered-content-cache-weight: 70", "rendered-content-cache-weight: 60"),
            ("long-sessions-100k.json", "hugo-assembly-thresholds", "output-dependency-depth", "output-dependency-depth: 50", "output-dependency-depth: 5"),
            ("long-sessions-100k.json", "hugo-assembly-thresholds", "collect-log-file-step-after", "collect-log-file-step-after: 200", "collect-log-file-step-after: 1000"),
            ("long-sessions-100k.json", "hugo-assembly-thresholds", "parallel-section-depth-after", "parallel-section-depth-after: 60", "parallel-section-depth-after: 3"),
            ("long-sessions-100k.json", "django-migration-naming", "name-limit-after-answer", "name-limit-after: 100", "name-limit-after: 52"),
            ("long-sessions-100k.json", "django-migration-naming", "fragment-join-limit", "fragment-join-limit: 52", "fragment-join-limit: 100"),
            ("long-sessions-100k.json", "django-migration-naming", "suggested-name-cap", "suggested-name-cap: 100", "suggested-name-cap: 52"),
            ("long-sessions-100k.json", "django-migration-naming", "index-or-constraint-dependency", "index-or-constraint-dependency: 5", "index-or-constraint-dependency: 4"),
            ("long-sessions-100k.json", "django-migration-naming", "questioner-abort-code", "questioner-abort-code: 3", "questioner-abort-code: 1"),
            ("long-sessions-100k.json", "django-migration-naming", "operation-writer-indent", "operation-writer-indent: 2", "operation-writer-indent: 4"),
            ("long-sessions-100k.json", "django-migration-naming", "name-limit-after", "name-limit-after: 100", "name-limit-after: 52"),
            ("long-sessions-200k.json", "jellyfin-scan-limits", "monitor-restart-delay-ms-after-answer", "monitor-restart-delay-ms-after: 2000", "monitor-restart-delay-ms-after: 1000"),
            ("long-sessions-200k.json", "jellyfin-scan-limits", "shortcut-extension", "shortcut-extension: .mblink", "shortcut-extension: .lnk"),
            ("long-sessions-200k.json", "jellyfin-scan-limits", "view-refresh-hours", "view-refresh-hours: 24", "view-refresh-hours: 12"),
            ("long-sessions-200k.json", "jellyfin-scan-limits", "validation-progress-share", "validation-progress-share: 96", "validation-progress-share: 100"),
            ("long-sessions-200k.json", "jellyfin-scan-limits", "monitor-restart-delay-ms", "monitor-restart-delay-ms: 1000", "monitor-restart-delay-ms: 2000"),
            ("long-sessions-200k.json", "jellyfin-scan-limits", "splashscreen-item-limit", "splashscreen-item-limit: 30", "splashscreen-item-limit: 32"),
            ("long-sessions-200k.json", "jellyfin-scan-limits", "splashscreen-max-rating", "splashscreen-max-rating: 13", "splashscreen-max-rating: 18"),
            ("long-sessions-200k.json", "jellyfin-scan-limits", "ignore-cache-floor", "ignore-cache-floor: 100", "ignore-cache-floor: 32"),
            ("long-sessions-200k.json", "jellyfin-scan-limits", "rules-cache-floor", "rules-cache-floor: 32", "rules-cache-floor: 24"),
            ("long-sessions-200k.json", "jellyfin-scan-limits", "splashscreen-item-limit-after", "splashscreen-item-limit-after: 32", "splashscreen-item-limit-after: 30"),
            ("long-sessions-200k.json", "jellyfin-scan-limits", "rules-cache-floor-after", "rules-cache-floor-after: 24", "rules-cache-floor-after: 32"),
            ("long-sessions-200k.json", "jellyfin-scan-limits", "monitor-restart-delay-ms-after", "monitor-restart-delay-ms-after: 2000", "monitor-restart-delay-ms-after: 1000"),
            ("long-sessions-200k.json", "redis-snapshot-thresholds", "sync-fsync-megabytes-after-answer", "sync-fsync-megabytes-after: 20", "sync-fsync-megabytes-after: 8"),
            ("long-sessions-200k.json", "redis-snapshot-thresholds", "snapshot-format-version", "snapshot-format-version: 16", "snapshot-format-version: 12"),
            ("long-sessions-200k.json", "redis-snapshot-thresholds", "hash-template-opcode", "hash-template-opcode: 242", "hash-template-opcode: 244"),
            ("long-sessions-200k.json", "redis-snapshot-thresholds", "array-type-code", "array-type-code: 28", "array-type-code: 21"),
            ("long-sessions-200k.json", "redis-snapshot-thresholds", "connset-preflush-kilobytes", "connset-preflush-kilobytes: 256", "connset-preflush-kilobytes: 8192"),
            ("long-sessions-200k.json", "redis-snapshot-thresholds", "compress-min-length", "compress-min-length: 20", "compress-min-length: 256"),
            ("long-sessions-200k.json", "redis-snapshot-thresholds", "empty-key-log-limit", "empty-key-log-limit: 10", "empty-key-log-limit: 100"),
            ("long-sessions-200k.json", "redis-snapshot-thresholds", "sync-fsync-megabytes", "sync-fsync-megabytes: 8", "sync-fsync-megabytes: 20"),
            ("long-sessions-200k.json", "redis-snapshot-thresholds", "rdbchannel-psync-code", "rdbchannel-psync-code: 6", "rdbchannel-psync-code: 5"),
            ("long-sessions-200k.json", "redis-snapshot-thresholds", "connset-preflush-kilobytes-after", "connset-preflush-kilobytes-after: 8192", "connset-preflush-kilobytes-after: 256"),
            ("long-sessions-200k.json", "redis-snapshot-thresholds", "sync-fsync-megabytes-after", "sync-fsync-megabytes-after: 20", "sync-fsync-megabytes-after: 8"),
            ("long-sessions-200k.json", "redis-snapshot-thresholds", "compress-min-length-after", "compress-min-length-after: 256", "compress-min-length-after: 20"),
            ("long-sessions-300k.json", "jellyfin-transcode-limits", "segment-gap-numerator-after-answer", "segment-gap-numerator-after: 128", "segment-gap-numerator-after: 24"),
            ("long-sessions-300k.json", "jellyfin-transcode-limits", "segmented-ping-timeout-ms", "segmented-ping-timeout-ms: 60000", "segmented-ping-timeout-ms: 10000"),
            ("long-sessions-300k.json", "jellyfin-transcode-limits", "partial-delete-retry-limit", "partial-delete-retry-limit: 10", "partial-delete-retry-limit: 24"),
            ("long-sessions-300k.json", "jellyfin-transcode-limits", "partial-delete-first-delay-ms", "partial-delete-first-delay-ms: 1500", "partial-delete-first-delay-ms: 15000"),
            ("long-sessions-300k.json", "jellyfin-transcode-limits", "sdr-image-timeout-ms", "sdr-image-timeout-ms: 10000", "sdr-image-timeout-ms: 20000"),
            ("long-sessions-300k.json", "jellyfin-transcode-limits", "hdr-image-timeout-ms", "hdr-image-timeout-ms: 20000", "hdr-image-timeout-ms: 10000"),
            ("long-sessions-300k.json", "jellyfin-transcode-limits", "segment-gap-numerator", "segment-gap-numerator: 24", "segment-gap-numerator: 128"),
            ("long-sessions-300k.json", "jellyfin-transcode-limits", "muxing-queue-floor", "muxing-queue-floor: 128", "muxing-queue-floor: 1024"),
            ("long-sessions-300k.json", "jellyfin-transcode-limits", "sane-bitrate-ceiling", "sane-bitrate-ceiling: 400000000", "sane-bitrate-ceiling: 200000000"),
            ("long-sessions-300k.json", "jellyfin-transcode-limits", "subtitle-description-length", "subtitle-description-length: 100", "subtitle-description-length: 1000"),
            ("long-sessions-300k.json", "jellyfin-transcode-limits", "partial-delete-retry-limit-after", "partial-delete-retry-limit-after: 24", "partial-delete-retry-limit-after: 10"),
            ("long-sessions-300k.json", "jellyfin-transcode-limits", "segment-gap-numerator-after", "segment-gap-numerator-after: 128", "segment-gap-numerator-after: 24"),
            ("long-sessions-300k.json", "jellyfin-transcode-limits", "sane-bitrate-ceiling-after", "sane-bitrate-ceiling-after: 200000000", "sane-bitrate-ceiling-after: 400000000"),
            ("long-sessions-300k.json", "redis-cluster-delays", "writable-delay-ms-after-answer", "writable-delay-ms-after: 4096", "writable-delay-ms-after: 2000"),
            ("long-sessions-300k.json", "redis-cluster-delays", "failover-relog-seconds", "failover-relog-seconds: 10", "failover-relog-seconds: 60"),
            ("long-sessions-300k.json", "redis-cluster-delays", "rcvbuf-init-bytes", "rcvbuf-init-bytes: 1024", "rcvbuf-init-bytes: 1000"),
            ("long-sessions-300k.json", "redis-cluster-delays", "accepts-per-call", "accepts-per-call: 1000", "accepts-per-call: 1024"),
            ("long-sessions-300k.json", "redis-cluster-delays", "config-line-slot-bytes", "config-line-slot-bytes: 128", "config-line-slot-bytes: 1024"),
            ("long-sessions-300k.json", "redis-cluster-delays", "rejoin-delay-max-ms", "rejoin-delay-max-ms: 5000", "rejoin-delay-max-ms: 500"),
            ("long-sessions-300k.json", "redis-cluster-delays", "rejoin-delay-min-ms", "rejoin-delay-min-ms: 500", "rejoin-delay-min-ms: 1024"),
            ("long-sessions-300k.json", "redis-cluster-delays", "writable-delay-ms", "writable-delay-ms: 2000", "writable-delay-ms: 4096"),
            ("long-sessions-300k.json", "redis-cluster-delays", "asm-aof-min-items", "asm-aof-min-items: 512", "asm-aof-min-items: 1000"),
            ("long-sessions-300k.json", "redis-cluster-delays", "rejoin-delay-min-ms-after", "rejoin-delay-min-ms-after: 1024", "rejoin-delay-min-ms-after: 500"),
            ("long-sessions-300k.json", "redis-cluster-delays", "writable-delay-ms-after", "writable-delay-ms-after: 4096", "writable-delay-ms-after: 2000"),
            ("long-sessions-300k.json", "redis-cluster-delays", "asm-aof-min-items-after", "asm-aof-min-items-after: 1000", "asm-aof-min-items-after: 512"),
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
    [InlineData("**eviction-ceiling:** 200")]
    [InlineData("`eviction-ceiling`: `200`")]
    [InlineData("- **eviction-ceiling**: 200.")]
    [InlineData("eviction-ceiling = 200 (hugolib/content_map_page.go)")]
    public void EvaluateCheck_KeyedFactWithMarkdownFormatting_Passes(string text)
    {
        var check = LoadTasks("long-sessions-100k.json").Single(task => task.TaskId == "hugo-assembly-thresholds").Checks!
            .Single(check => check.Id == "eviction-ceiling");

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
