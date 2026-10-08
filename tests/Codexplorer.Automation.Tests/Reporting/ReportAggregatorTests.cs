using Codexplorer.Automation.Reporting;
using Codexplorer.Measurements;
using FluentAssertions;

namespace Codexplorer.Automation.Tests.Reporting;

/// <summary>
///     Represents deterministic coverage of report aggregation.
/// </summary>
/// <remarks>Tests exercise public behavior through deterministic measurement fixtures.</remarks>
public sealed class ReportAggregatorTests
{
    /// <summary>
    ///     Verifies that uses only completed pairs and nearest rank estimator statistics.
    /// </summary>
    [Fact]
    public void CreateTask_UsesOnlyCompletedPairsAndNearestRankEstimatorStatistics()
    {
        var measurements = new SessionMeasurements
        {
            Complete = true,
            PrepareRecords = Enumerable.Range(1, 20).Select(index => new PrepareMeasurement
            {
                Index = index, Turn = index + 3, Status = "completed", Outcome = "Ready", TokensBefore = 120, TokensAfter = index
            }).Append(new PrepareMeasurement { Index = 21 }).ToArray(),
            ProviderCalls = Enumerable.Range(1, 20).Select(index => new ProviderCallMeasurement
            {
                TranscriptIndex = index, PrepareIndex = index, Status = "completed", InputTokens = 100, OutputTokens = 2
            }).Append(new ProviderCallMeasurement { PrepareIndex = 21, TranscriptIndex = 21, Status = "cancelled" }).ToArray()
        };

        var result = new ReportAggregator().CreateTask("task", "small", "reply_received", true, 24, measurements, [], 0, null);

        result.Metrics.EstimatorPairedTurnCount.Should().Be(20);
        result.Metrics.EstimatorSignedMean.Should().BeApproximately(0.895, 0.000001);
        result.Metrics.EstimatorSignedP95.Should().Be(0.98);
        result.Metrics.PrepareCalls.Should().Be(21);
        result.Metrics.CompletedPrepareCalls.Should().Be(20);
        result.TokenGuardTranscriptOffset.Should().Be(3);
    }

    /// <summary>
    ///     Verifies that missing attempt usage is unavailable and does not enter estimator population.
    /// </summary>
    [Fact]
    public void CreateTask_MissingAttemptUsageIsUnavailableAndDoesNotEnterEstimatorPopulation()
    {
        var measurements = new SessionMeasurements
        {
            PrepareRecords = [new PrepareMeasurement { Index = 1, Status = "completed", Outcome = "Ready", TokensBefore = 0, TokensAfter = 0 }],
            ProviderCalls = [new ProviderCallMeasurement { PrepareIndex = 1, TranscriptIndex = 1, Status = "failed" }],
            SummarizerCalls = 1
        };

        var result = new ReportAggregator().CreateTask("task", "small", "failed", false, 1, measurements, [], 1, null);

        result.Metrics.ProviderInputTokens.Should().BeNull();
        result.Metrics.ProviderMissingInputUsageCalls.Should().Be(1);
        result.Metrics.SummarizerInputTokens.Should().BeNull();
        result.Metrics.HelperInputTokens.Should().BeNull();
        result.Metrics.EstimatedPromptTokenReduction.Should().BeNull();
        result.Metrics.EstimatorPairedTurnCount.Should().Be(0);
        result.Metrics.EstimatorSignedP95.Should().BeNull();
    }

    /// <summary>
    ///     Verifies that zero based transcript index retains its measured turn offset.
    /// </summary>
    [Fact]
    public void Validate_ZeroBasedTranscriptIndexRetainsItsMeasuredTurnOffset()
    {
        var report = ReportFixture.Create();
        var task = report.Tasks[0];
        var measurements = ReportAggregator.ToMeasurements(task) with
        {
            ProviderCalls = [task.ProviderCalls[0] with { TranscriptIndex = 0 }]
        };
        var aggregator = new ReportAggregator();
        var replacement = aggregator.CreateTask("task", "small", "reply_received", true, 24, measurements, [], 0, null);
        var updated = aggregator.CreateReport(report.Run, [replacement], [], false);

        ReportValidator.Validate(updated).Should().BeEmpty();
        updated.Tasks[0].TokenGuardTranscriptOffset.Should().Be(2);
    }

    /// <summary>
    ///     Verifies that uses the combined paired population for signed and absolute statistics.
    /// </summary>
    [Fact]
    public void CreateReport_UsesTheCombinedPairedPopulationForSignedAndAbsoluteStatistics()
    {
        var first = ReportFixture.Create(inputTokens: 100, after: 150, taskId: "first");
        var second = ReportFixture.Create(inputTokens: 100, after: 50, taskId: "second");

        var result = new ReportAggregator().CreateReport(first.Run, [first.Tasks[0], second.Tasks[0]], [], false);

        result.Totals.Metrics.EstimatorSignedMean.Should().Be(0);
        result.Totals.Metrics.EstimatorSignedP95.Should().Be(0.5);
        result.Totals.Metrics.EstimatorAbsoluteMean.Should().Be(0.5);
        result.Totals.Metrics.EstimatorAbsoluteP95.Should().Be(0.5);
        result.Totals.Metrics.EstimatorPairedTurnCount.Should().Be(2);
    }

    /// <summary>
    ///     Verifies that varying turn offsets preserve individual pairs and have no task offset.
    /// </summary>
    [Fact]
    public void CreateTask_VaryingTurnOffsetsPreserveIndividualPairsAndHaveNoTaskOffset()
    {
        var template = ReportFixture.Create().Tasks[0];
        var measurements = ReportAggregator.ToMeasurements(template) with
        {
            PrepareRecords = [template.PrepareRecords[0], template.PrepareRecords[0] with { Index = 2, Turn = 5 }],
            ProviderCalls = [template.ProviderCalls[0], template.ProviderCalls[0] with { PrepareIndex = 2, TranscriptIndex = 2 }]
        };

        var result = new ReportAggregator().CreateTask("task", "small", "reply_received", true, 24, measurements, [], 0, null);

        result.TokenGuardTranscriptOffset.Should().BeNull();
        result.ProviderCalls.Select(call => call.PrepareIndex).Should().Equal(1, 2);
    }

    /// <summary>
    ///     Verifies that failed task and unrun tasks preserve partial coverage.
    /// </summary>
    [Fact]
    public void CreateReport_FailedTaskAndUnrunTasksPreservePartialCoverage()
    {
        var report = ReportFixture.Create();

        var result = new ReportAggregator().CreateReport(report.Run,
            [report.Tasks[0] with { Outcome = "failed", ProtocolCompletion = false }], ["remaining"], false);

        result.Partial.Should().BeTrue();
        result.UnrunTaskIds.Should().Equal("remaining");
        result.Totals.ProtocolCompletionRate.Should().Be(0);
        result.Totals.TaskCount.Should().Be(1);
    }

    /// <summary>
    ///     Verifies that empty run has unavailable ratios and zero known usage.
    /// </summary>
    [Fact]
    public void CreateReport_EmptyRunHasUnavailableRatiosAndZeroKnownUsage()
    {
        var report = ReportFixture.Create();

        var result = new ReportAggregator().CreateReport(report.Run, [], [], false);

        result.Totals.ProtocolCompletionRate.Should().BeNull();
        result.Totals.Metrics.EstimatedPromptTokenReduction.Should().BeNull();
        result.Totals.Metrics.EstimatorPairedTurnCount.Should().Be(0);
        result.Totals.Metrics.ProviderInputTokens.Should().Be(0);
    }

    /// <summary>
    ///     Verifies that failed and cancelled calls consume budget without completing turns.
    /// </summary>
    [Fact]
    public void CreateTask_FailedAndCancelledCallsConsumeBudgetWithoutCompletingTurns()
    {
        var task = ReportFixture.Create().Tasks[0];
        var measurements = ReportAggregator.ToMeasurements(task) with
        {
            PrepareRecords = [task.PrepareRecords[0], task.PrepareRecords[0] with { Index = 2 }, task.PrepareRecords[0] with { Index = 3 }],
            ProviderCalls = [task.ProviderCalls[0], task.ProviderCalls[0] with { PrepareIndex = 2, TranscriptIndex = 2, Status = "failed" },
                task.ProviderCalls[0] with { PrepareIndex = 3, TranscriptIndex = 3, Status = "cancelled" }]
        };

        var result = new ReportAggregator().CreateTask("task", "small", "turn_budget_reached", false, 2, measurements, [], 0, null);

        result.Metrics.ModelCallsMade.Should().Be(3);
        result.Metrics.CompletedModelTurns.Should().Be(1);
        result.Metrics.BudgetOvershoot.Should().Be(1);
        result.Metrics.EstimatorPairedTurnCount.Should().Be(1);
    }

    /// <summary>
    ///     Verifies that strategy that increases tokens preserves negative reclaimed count.
    /// </summary>
    [Fact]
    public void CreateTask_StrategyThatIncreasesTokensPreservesNegativeReclaimedCount()
    {
        var template = ReportFixture.Create(after: 150).Tasks[0];
        var measurements = ReportAggregator.ToMeasurements(template) with
        {
            PrepareRecords = [template.PrepareRecords[0] with { StrategyRuns = 1 }]
        };

        var result = new ReportAggregator().CreateTask("task", "small", "reply_received", true, 24, measurements, [], 0, null);

        result.Metrics.TokensReclaimed.Should().Be(-50);
    }
}
