using System.Text.Json;
using Codexplorer.Automation.Configuration;
using Codexplorer.Automation.Reporting;
using Codexplorer.Automation.Scoring;
using Codexplorer.Measurements;

namespace Codexplorer.Automation.Tests.Reporting;

/// <summary>
///     Provides deterministic report fixtures.
/// </summary>
/// <remarks>Tests exercise public behavior through deterministic measurement fixtures.</remarks>
internal static class ReportFixture
{
    /// <summary>
    ///     Creates a deterministic report fixture with complete provider usage.
    /// </summary>
    /// <param name="arm">The treatment or control arm.</param>
    /// <param name="inputTokens">The provider input usage for the fixture.</param>
    /// <param name="after">The prepared token estimate for the fixture.</param>
    /// <param name="taskId">The manifest task identifier.</param>
    /// <param name="cachedInputTokens">The cached input usage of the call, or <see langword="null" /> when the provider reported none.</param>
    /// <returns>A deterministic valid report.</returns>
    public static RunReport Create(string arm = "treatment", long inputTokens = 100, long after = 80, string taskId = "task",
        long? cachedInputTokens = null)
    {
        var aggregator = new ReportAggregator();
        var task = aggregator.CreateTask(taskId, "small", "reply_received", true, 24, new SessionMeasurements
        {
            Complete = true, SummaryCrossCheck = "matched",
            PrepareRecords = [new PrepareMeasurement
            {
                Index = 1, Turn = 2, Status = "completed", OpeningMessagePresent = true, Outcome = "Ready", TokensBefore = 100, TokensAfter = after
            }],
            ProviderCalls = [new ProviderCallMeasurement
            {
                PrepareIndex = 1, TranscriptIndex = 1, Status = "completed", InputTokens = inputTokens, OutputTokens = 5,
                CachedInputTokens = cachedInputTokens
            }]
        }, [], 0, null, new AnswerScoringResult { Checks = [], Probe = null });
        return aggregator.CreateReport(new RunMetadata
        {
            RunId = "20261007-100000-" + arm, CaptureEnabled = true, CommitSha = new string('a', 40), RepositoryDirty = false,
            StartedAtUtc = DateTimeOffset.Parse("2026-10-07T10:00:00Z"), EndedAtUtc = DateTimeOffset.Parse("2026-10-07T10:01:00Z"),
            EffectiveSettings = new EffectiveSettings
            {
                AgentModel = "agent", SummarizerModel = "summary", ContextWindowTokens = 1000, MaxOutputTokens = 100,
                ExchangeMaxTurns = 10, SoftThresholdRatio = 0.7, HardThresholdRatio = 0.9,
                WindowSize = 3, SummaryWindowSize = 3, MinSummaryTokens = 10, MaxSummaryTokens = 100
            },
            HelperModel = "helper", HelperMaxOutputTokens = 100, HelperTemperature = 0,
            TurnBudgets = new AutomationTurnBudgetOptions(), Arm = arm, ManifestPath = "tasks/test.json",
            ManifestSha256 = new string('b', 64), ManifestProvenance = "file"
        }, [task], [], false);
    }

    /// <summary>Gets the path of the copied control report that the harness recorded in schema version 4.</summary>
    /// <returns>The absolute path of the recorded report in the test output folder.</returns>
    public static string RecordedSchemaVersionFourPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "Reporting", "Fixtures", "run-report-20261010-083413-control.json");
    }

    /// <summary>Creates a valid report with explicit scoring and opening-message evidence.</summary>
    /// <param name="checks">The check results.</param>
    /// <param name="probe">The optional probe result.</param>
    /// <param name="opening">Whether the unchanged opening survived.</param>
    /// <param name="protocol">Whether a final wrap-up reply completed.</param>
    /// <param name="maskingOnly">Whether the task masked messages without summarizing or dropping any.</param>
    /// <returns>The reconstructed report.</returns>
    public static RunReport Scored(IReadOnlyList<CheckResult> checks, ProbeResult? probe = null, bool opening = false, bool protocol = true,
        bool maskingOnly = false)
    {
        var report = Create();
        var template = report.Tasks[0];
        var measurements = ReportAggregator.ToMeasurements(template) with
        {
            MessagesMasked = 2, MessagesSummarized = maskingOnly ? 0 : 3, MessagesDropped = maskingOnly ? 0 : 4,
            PrepareRecords = [template.PrepareRecords[0] with { OpeningMessagePresent = opening }],
        };
        var aggregator = new ReportAggregator();
        var task = aggregator.CreateTask(template.TaskId, template.Size, protocol ? "reply_received" : "failed", protocol, 24,
            measurements, [], 0, null, new AnswerScoringResult { Checks = checks, Probe = probe });
        return aggregator.CreateReport(report.Run, [task], [], false);
    }

    /// <summary>Relabels a current report as schema version 4, 5 or 6 and clears the totals field that version does not have.</summary>
    /// <param name="report">The current report, with no cached input tokens when <paramref name="schemaVersion" /> is 4.</param>
    /// <param name="schemaVersion">The older schema version, 4, 5 or 6.</param>
    /// <returns>The report as a harness of that schema version would have built it.</returns>
    public static RunReport AsOlderSchemaVersion(RunReport report, int schemaVersion)
    {
        return report with
        {
            SchemaVersion = schemaVersion,
            Totals = report.Totals with
            {
                NotApplicableProbeCount = schemaVersion < 6 ? null : report.Totals.NotApplicableProbeCount, MaskedResultProbes = null
            }
        };
    }

    /// <summary>Serializes a report in the JSON shape of schema version 4, 5 or 6, which omits the fields later versions added.</summary>
    /// <param name="report">The current report, with no cached input tokens when <paramref name="schemaVersion" /> is 4.</param>
    /// <param name="schemaVersion">The older schema version, 4, 5 or 6.</param>
    /// <returns>The report JSON as a harness of that schema version would have written it.</returns>
    public static string SerializeAsOlderSchemaVersion(RunReport report, int schemaVersion)
    {
        var root = JsonSerializer.SerializeToNode(AsOlderSchemaVersion(report, schemaVersion), ReportJson.Options)!;
        if (schemaVersion < 6)
            root["totals"]!.AsObject().Remove("notApplicableProbeCount");
        root["totals"]!.AsObject().Remove("maskedResultProbes");
        foreach (var task in root["tasks"]!.AsArray())
        {
            if (task!["probe"] is { } probe)
            {
                probe.AsObject().Remove("kind");
                probe.AsObject().Remove("refetched");
            }
            foreach (var prepare in task["prepareRecords"]!.AsArray())
                foreach (var field in new[] { "probeValueState", "probeFactVisibleElsewhere", "probeQuestionAsked", "probeRefetched" })
                    prepare!.AsObject().Remove(field);
        }
        if (schemaVersion == 4)
        {
            root["totals"]!["metrics"]!.AsObject().Remove("providerCachedInputTokens");
            foreach (var task in root["tasks"]!.AsArray())
            {
                task!["metrics"]!.AsObject().Remove("providerCachedInputTokens");
                foreach (var call in task["providerCalls"]!.AsArray())
                {
                    call!.AsObject().Remove("cachedInputTokens");
                }
            }
        }

        return root.ToJsonString(ReportJson.Options);
    }
}
