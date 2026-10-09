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
    /// <returns>A deterministic valid report.</returns>
    public static RunReport Create(string arm = "treatment", long inputTokens = 100, long after = 80, string taskId = "task")
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
                PrepareIndex = 1, TranscriptIndex = 1, Status = "completed", InputTokens = inputTokens, OutputTokens = 5
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
    /// <summary>Creates a valid report with explicit scoring and opening-message evidence.</summary>
    /// <param name="checks">The check results.</param>
    /// <param name="probe">The optional probe result.</param>
    /// <param name="opening">Whether the unchanged opening survived.</param>
    /// <param name="protocol">Whether a final wrap-up reply completed.</param>
    /// <returns>The reconstructed report.</returns>
    public static RunReport Scored(IReadOnlyList<CheckResult> checks, ProbeResult? probe = null, bool opening = false, bool protocol = true)
    {
        var report = Create();
        var template = report.Tasks[0];
        var measurements = ReportAggregator.ToMeasurements(template) with
        {
            MessagesMasked = 2, MessagesSummarized = 3, MessagesDropped = 4,
            PrepareRecords = [template.PrepareRecords[0] with { OpeningMessagePresent = opening }],
        };
        var aggregator = new ReportAggregator();
        var task = aggregator.CreateTask(template.TaskId, template.Size, protocol ? "reply_received" : "failed", protocol, 24,
            measurements, [], 0, null, new AnswerScoringResult { Checks = checks, Probe = probe });
        return aggregator.CreateReport(report.Run, [task], [], false);
    }

}
