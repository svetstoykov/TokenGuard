using System.Text.Json;
using Codexplorer.Automation.Comparison;
using Codexplorer.Automation.Reporting;
using Codexplorer.Automation.Tests.Reporting;
using FluentAssertions;

namespace Codexplorer.Automation.Tests.Comparison;

/// <summary>
///     Represents deterministic coverage of report comparison.
/// </summary>
/// <remarks>Tests exercise public behavior through deterministic measurement fixtures.</remarks>
public sealed class ComparisonCommandTests
{
    /// <summary>
    ///     Verifies that positive provider input delta exceeds absolute limit.
    /// </summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task RunAsync_PositiveProviderInputDeltaExceedsAbsoluteLimit()
    {
        var result = await CompareAsync(ReportFixture.Create(inputTokens: 100), ReportFixture.Create(inputTokens: 130),
            ["--limit", "providerInputTokens=20"]);

        result.ExitCode.Should().Be(1);
        result.Text.Should().Contain("providerInputTokens: baseline=100 candidate=130 delta=30").And.Contain("Regression limit exceeded");
    }

    /// <summary>
    ///     Verifies that compatible control pair prints measured reduction and work caveat.
    /// </summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task RunAsync_CompatibleControlPairPrintsMeasuredReductionAndWorkCaveat()
    {
        var result = await CompareAsync(ReportFixture.Create("control", 200), ReportFixture.Create("treatment", 100));

        result.ExitCode.Should().Be(0);
        result.Text.Should().Contain("measuredProviderInputReduction=0.5").And.Contain("does not establish equivalent work");
    }

    /// <summary>
    ///     Verifies that modified totals fail validation.
    /// </summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task RunAsync_ModifiedTotalsFailValidation()
    {
        var baseline = ReportFixture.Create();
        var candidate = baseline with { Totals = baseline.Totals with { Metrics = baseline.Totals.Metrics with { ProviderInputTokens = 99 } } };

        var result = await CompareAsync(baseline, candidate);

        result.ExitCode.Should().Be(1);
        result.Text.Should().Contain("inconsistent");
    }

    /// <summary>
    ///     Verifies that invalid control cannot be overridden.
    /// </summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task RunAsync_InvalidControlCannotBeOverridden()
    {
        var report = ReportFixture.Create("control");
        var task = report.Tasks[0];
        var invalid = task with
        {
            PrepareRecords = [task.PrepareRecords[0] with { StrategyRuns = 1 }],
            Metrics = task.Metrics with { StrategyRuns = 1 }
        };
        var candidate = new ReportAggregator().CreateReport(report.Run, [invalid], [], false);

        var result = await CompareAsync(ReportFixture.Create(), candidate, ["--allow-incompatible"]);

        result.ExitCode.Should().Be(1);
        result.Text.Should().Contain("Control task").And.NotContain("measuredProviderInputReduction=");
    }

    /// <summary>
    ///     Verifies that task alignment uses ids and displays distribution union.
    /// </summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task RunAsync_TaskAlignmentUsesIdsAndDisplaysDistributionUnion()
    {
        var baseline = ReportFixture.Create(taskId: "baseline-only");
        var candidate = ReportFixture.Create(taskId: "candidate-only");
        var task = candidate.Tasks[0];
        var measured = new ReportAggregator().CreateTask(task.TaskId, task.Size, task.Outcome, task.ProtocolCompletion, task.ModelCallBudget,
            ReportAggregator.ToMeasurements(task) with { HealthSignalCounts = new Dictionary<string, long> { ["warning"] = 2 } }, [], 0, null);
        candidate = new ReportAggregator().CreateReport(candidate.Run, [measured], [], false);

        var result = await CompareAsync(baseline, candidate);

        result.ExitCode.Should().Be(0);
        result.Text.Should().Contain("Baseline-only tasks: baseline-only").And.Contain("Candidate-only tasks: candidate-only")
            .And.Contain("healthSignalCounts.warning: baseline=0 candidate=2 delta=2");
    }

    /// <summary>
    ///     Verifies that a new health signal is compared against a zero baseline for regression limits.
    /// </summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <param name="threshold">The allowed absolute increase in signal count.</param>
    /// <param name="expectedExit">The expected comparison exit code.</param>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 0)]
    public async Task RunAsync_NewDistributionEntryUsesZeroBaselineForLimits(int threshold, int expectedExit)
    {
        var baseline = ReportFixture.Create();
        var task = baseline.Tasks[0];
        var aggregator = new ReportAggregator();
        var measured = aggregator.CreateTask(task.TaskId, task.Size, task.Outcome, task.ProtocolCompletion, task.ModelCallBudget,
            ReportAggregator.ToMeasurements(task) with { HealthSignalCounts = new Dictionary<string, long> { ["warning"] = 2 } }, [], 0, null);
        var candidate = aggregator.CreateReport(baseline.Run, [measured], [], false);

        var result = await CompareAsync(baseline, candidate, ["--limit", $"healthSignalCounts.warning={threshold}"]);

        result.ExitCode.Should().Be(expectedExit);
        result.Text.Should().Contain("healthSignalCounts.warning: baseline=0 candidate=2 delta=2")
            .And.NotContain("Unknown or informational metric");
    }

    /// <summary>
    ///     Verifies that a disappearing health signal is a reduction rather than an unknown limit metric.
    /// </summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task RunAsync_DisappearingDistributionEntryUsesZeroCandidateForLimits()
    {
        var candidate = ReportFixture.Create();
        var task = candidate.Tasks[0];
        var aggregator = new ReportAggregator();
        var measured = aggregator.CreateTask(task.TaskId, task.Size, task.Outcome, task.ProtocolCompletion, task.ModelCallBudget,
            ReportAggregator.ToMeasurements(task) with { HealthSignalCounts = new Dictionary<string, long> { ["warning"] = 2 } }, [], 0, null);
        var baseline = aggregator.CreateReport(candidate.Run, [measured], [], false);

        var result = await CompareAsync(baseline, candidate, ["--limit", "healthSignalCounts.warning=0"]);

        result.ExitCode.Should().Be(0);
        result.Text.Should().Contain("healthSignalCounts.warning: baseline=2 candidate=0 delta=-2");
    }

    /// <summary>Verifies an absent known distribution entry counts as zero in both reports.</summary>
    /// <param name="metric">The known outcome or health signal counter.</param>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Theory]
    [InlineData("prepareOutcomeCounts.Compacted")]
    [InlineData("prepareOutcomeCounts.CompactionInsufficient")]
    [InlineData("prepareOutcomeCounts.CannotCompact")]
    [InlineData("healthSignalCounts.EstimatorDrift")]
    [InlineData("healthSignalCounts.RepeatedCompaction")]
    [InlineData("healthSignalCounts.LowCompactionYield")]
    [InlineData("healthSignalCounts.SummarizationFailureStreak")]
    [InlineData("healthSignalCounts.RepeatedOverBudget")]
    [InlineData("healthSignalCounts.PinnedPressure")]
    [InlineData("healthSignalCounts.CheckpointChurn")]
    public async Task RunAsync_DistributionEntryAbsentOnBothSides_UsesZeroForLimits(string metric)
    {
        var result = await CompareAsync(ReportFixture.Create(), ReportFixture.Create(), ["--limit", metric + "=0"]);

        result.ExitCode.Should().Be(0);
        result.Text.Should().Contain(metric + ": baseline=0 candidate=0 delta=0").And.NotContain("Unknown or informational metric");
    }

    /// <summary>Verifies unrun tasks cannot pass regression limits without an explicit override.</summary>
    /// <param name="allowIncompatible">Whether incomplete coverage is explicitly permitted.</param>
    /// <param name="expectedExit">The expected comparison exit code.</param>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public async Task RunAsync_UnrunCandidateTasks_RequireOverrideForLimits(bool allowIncompatible, int expectedExit)
    {
        var baseline = ReportFixture.Create();
        var candidate = new ReportAggregator().CreateReport(baseline.Run, [], ["task"], false);
        var options = new List<string> { "--limit", "modelCallsMade=0", "--limit", "tokensAfter=0" };
        if (allowIncompatible)
        {
            options.Add("--allow-incompatible");
        }

        var result = await CompareAsync(baseline, candidate, options.ToArray());

        result.ExitCode.Should().Be(expectedExit);
        result.Text.Should().Contain("Candidate partial: True").And.Contain("Candidate unrun tasks: task")
            .And.Contain("Regression limits require complete runs with matching task coverage");
        if (allowIncompatible)
        {
            result.Text.Should().Contain("Compatibility override enabled; regression limits use incomplete or differing task coverage");
        }
    }

    /// <summary>Verifies different task IDs cannot pass regression limits without an explicit override.</summary>
    /// <param name="allowIncompatible">Whether differing coverage is explicitly permitted.</param>
    /// <param name="expectedExit">The expected comparison exit code.</param>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public async Task RunAsync_DifferentTaskSets_RequireOverrideForLimits(bool allowIncompatible, int expectedExit)
    {
        var options = allowIncompatible ? new[] { "--limit", "modelCallsMade=0", "--allow-incompatible" }
            : ["--limit", "modelCallsMade=0"];

        var result = await CompareAsync(ReportFixture.Create(taskId: "baseline-only"), ReportFixture.Create(taskId: "candidate-only"), options);

        result.ExitCode.Should().Be(expectedExit);
        result.Text.Should().Contain("Baseline-only tasks: baseline-only").And.Contain("Candidate-only tasks: candidate-only")
            .And.Contain("Regression limits require complete runs with matching task coverage");
    }

    /// <summary>Verifies a partial report cannot pass regression limits even when task IDs match.</summary>
    /// <param name="partialBaseline">Whether the baseline rather than the candidate contains a task failure.</param>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_PartialReportWithMatchingTasks_RejectsLimits(bool partialBaseline)
    {
        var complete = ReportFixture.Create();
        var partial = new ReportAggregator().CreateReport(complete.Run,
            [complete.Tasks[0] with { Outcome = "failed", ProtocolCompletion = false }], [], false);

        var result = await CompareAsync(partialBaseline ? partial : complete, partialBaseline ? complete : partial,
            ["--limit", "modelCallsMade=0"]);

        result.ExitCode.Should().Be(1);
        result.Text.Should().Contain("Regression limits require complete runs with matching task coverage");
    }

    /// <summary>Verifies incomplete task measurements cannot pass count limits.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task RunAsync_IncompleteMeasurements_RejectsLimits()
    {
        var baseline = ReportFixture.Create();
        var candidate = new ReportAggregator().CreateReport(baseline.Run,
            [baseline.Tasks[0] with { MeasurementsComplete = false, SummaryCrossCheck = "pending" }], [], false);

        var result = await CompareAsync(baseline, candidate, ["--limit", "modelCallsMade=0"]);

        result.ExitCode.Should().Be(1);
        result.Text.Should().Contain("Regression limits require complete runs with matching task coverage");
    }

    /// <summary>Verifies informational comparisons display partial and unrun coverage.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task RunAsync_UnrunCandidateTasksWithoutLimits_PrintsCoverage()
    {
        var baseline = ReportFixture.Create();
        var candidate = new ReportAggregator().CreateReport(baseline.Run, [], ["task"], false);

        var result = await CompareAsync(baseline, candidate);

        result.ExitCode.Should().Be(0);
        result.Text.Should().Contain("Baseline partial: False").And.Contain("Candidate partial: True")
            .And.Contain("Candidate unrun tasks: task").And.Contain("Baseline-only tasks: task");
    }

    /// <summary>
    ///     Verifies that invalid limit returns failure.
    /// </summary>
    /// <param name="limit">The metric and configured regression delta.</param>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Theory]
    [InlineData("estimatorSignedMean=1")]
    [InlineData("unknown=1")]
    [InlineData("prepareOutcomeCounts.CannotCompcat=0")]
    [InlineData("healthSignalCounts.RepeatedOverBuget=0")]
    [InlineData("healthSignalCounts.=0")]
    [InlineData("providerInputTokens=-1")]
    [InlineData("providerInputTokens=NaN")]
    [InlineData("providerInputTokens=Infinity")]
    public async Task RunAsync_InvalidLimitReturnsFailure(string limit)
    {
        var result = await CompareAsync(ReportFixture.Create(), ReportFixture.Create(), ["--limit", limit]);

        result.ExitCode.Should().Be(1);
    }

    /// <summary>
    ///     Verifies that incompatibility requires override and prints mismatch.
    /// </summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task RunAsync_IncompatibilityRequiresOverrideAndPrintsMismatch()
    {
        var baseline = ReportFixture.Create();
        var candidate = baseline with { Run = baseline.Run with { HelperTemperature = 0.5 } };

        var result = await CompareAsync(baseline, candidate);
        var overridden = await CompareAsync(baseline, candidate, ["--allow-incompatible"]);

        result.ExitCode.Should().Be(1);
        result.Text.Should().Contain("Incompatible: helperTemperature");
        overridden.ExitCode.Should().Be(0);
        overridden.Text.Should().Contain("Compatibility override");
    }

    /// <summary>
    ///     Verifies that context window difference is allowed only across arms.
    /// </summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task RunAsync_ContextWindowDifferenceIsAllowedOnlyAcrossArms()
    {
        var control = ReportFixture.Create("control") with
        {
            Run = ReportFixture.Create("control").Run with
            {
                EffectiveSettings = ReportFixture.Create().Run.EffectiveSettings with { ContextWindowTokens = 10000 }
            }
        };

        var acrossArms = await CompareAsync(control, ReportFixture.Create());
        var sameArms = await CompareAsync(control with { Run = control.Run with { Arm = "treatment" } }, ReportFixture.Create());

        acrossArms.ExitCode.Should().Be(0);
        sameArms.ExitCode.Should().Be(1);
        sameArms.Text.Should().Contain("contextWindowTokens");
    }

    /// <summary>
    ///     Verifies that unavailable usage cannot have regression limit.
    /// </summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task RunAsync_UnavailableUsageCannotHaveRegressionLimit()
    {
        var baseline = ReportFixture.Create();
        var task = baseline.Tasks[0];
        var aggregator = new ReportAggregator();
        var unknown = aggregator.CreateTask(task.TaskId, task.Size, task.Outcome, task.ProtocolCompletion, task.ModelCallBudget,
            ReportAggregator.ToMeasurements(task) with { ProviderCalls = [task.ProviderCalls[0] with { InputTokens = null }] }, [], 0, null);
        var candidate = aggregator.CreateReport(baseline.Run, [unknown], [], false);

        var result = await CompareAsync(baseline, candidate, ["--limit", "providerInputTokens=10"]);

        result.ExitCode.Should().Be(1);
        result.Text.Should().Contain("providerInputTokens: baseline=100 candidate=unavailable delta=unavailable")
            .And.Contain("Regression limit metric is unavailable");
    }

    /// <summary>
    ///     Verifies that ratio limits use the metric regression direction.
    /// </summary>
    /// <param name="limit">The metric and configured regression delta.</param>
    /// <param name="after">The prepared token estimate for the fixture.</param>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Theory]
    [InlineData("estimatedPromptTokenReduction=0.05", 90)]
    [InlineData("estimatorAbsoluteMean=0.05", 60)]
    public async Task RunAsync_RatioLimitsUseTheMetricRegressionDirection(string limit, long after)
    {
        var result = await CompareAsync(ReportFixture.Create(after: 80), ReportFixture.Create(after: after), ["--limit", limit]);

        result.ExitCode.Should().Be(1);
        result.Text.Should().Contain("Regression limit exceeded");
    }

    /// <summary>
    ///     Verifies that protocol completion decrease exceeds its limit.
    /// </summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task RunAsync_ProtocolCompletionDecreaseExceedsItsLimit()
    {
        var baseline = ReportFixture.Create();
        var candidate = new ReportAggregator().CreateReport(baseline.Run,
            [baseline.Tasks[0] with { Outcome = "turn_budget_reached", ProtocolCompletion = false }], [], false);

        var result = await CompareAsync(baseline, candidate, ["--limit", "protocolCompletionRate=0.5"]);

        result.ExitCode.Should().Be(1);
        result.Text.Should().Contain("Regression limit exceeded: protocolCompletionRate");
    }

    /// <summary>
    ///     Verifies that measured reduction shows outcome differences for complete failed treatment.
    /// </summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task RunAsync_MeasuredReductionShowsOutcomeDifferencesForCompleteFailedTreatment()
    {
        var control = ReportFixture.Create("control", 200);
        var candidate = ReportFixture.Create("treatment", 100);
        candidate = new ReportAggregator().CreateReport(candidate.Run,
            [candidate.Tasks[0] with { Outcome = "failed", ProtocolCompletion = false }], [], false);

        var result = await CompareAsync(control, candidate);

        result.ExitCode.Should().Be(0);
        result.Text.Should().Contain("measuredProviderInputReduction=0.5").And.Contain("OUTCOME DIFFERS");
    }

    /// <summary>
    ///     Verifies that malformed required shape returns failure.
    /// </summary>
    /// <param name="corruption">The JSON shape corruption applied to the fixture.</param>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("nonfinite")]
    public async Task RunAsync_MalformedRequiredShapeReturnsFailure(string corruption)
    {
        var json = JsonSerializer.Serialize(ReportFixture.Create(), ReportJson.Options);
        json = corruption switch
        {
            "missing" => json.Replace("\"strategyRuns\": 0,", "", StringComparison.Ordinal),
            "duplicate" => json.Replace("\"healthSignalCounts\": {}", "\"healthSignalCounts\": {\"same\":1,\"same\":2}",
                StringComparison.Ordinal),
            _ => json.Replace("\"protocolCompletionRate\": 1", "\"protocolCompletionRate\": 1e999", StringComparison.Ordinal)
        };
        var directory = Path.Combine(Path.GetTempPath(), "tg-invalid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "report.json");
            await File.WriteAllTextAsync(path, json);
            using var output = new StringWriter();

            var result = await ComparisonCommand.RunAsync([path, path], output);

            result.Should().Be(1);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    ///     Asynchronously compares deterministic report fixtures through JSON files.
    /// </summary>
    /// <param name="baseline">The baseline report fixture. Cannot be <see langword="null" />.</param>
    /// <param name="candidate">The candidate report fixture. Cannot be <see langword="null" />.</param>
    /// <param name="extra">Additional comparison options, or <see langword="null" /> for none.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the exit code and comparison text.</returns>
    internal static async Task<(int ExitCode, string Text)> CompareAsync(RunReport baseline, RunReport candidate, string[]? extra = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), "tg-compare-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var baselinePath = Path.Combine(directory, "baseline.json");
            var candidatePath = Path.Combine(directory, "candidate.json");
            await File.WriteAllTextAsync(baselinePath, JsonSerializer.Serialize(baseline, ReportJson.Options));
            await File.WriteAllTextAsync(candidatePath, JsonSerializer.Serialize(candidate, ReportJson.Options));
            using var output = new StringWriter();
            var exit = await ComparisonCommand.RunAsync([baselinePath, candidatePath, .. extra ?? []], output);
            return (exit, output.ToString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
