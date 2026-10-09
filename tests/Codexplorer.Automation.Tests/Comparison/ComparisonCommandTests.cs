using Codexplorer.Automation.Scoring;
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
            ReportAggregator.ToMeasurements(task) with { HealthSignalCounts = new Dictionary<string, long> { ["warning"] = 2 } }, [], 0, null,
            new AnswerScoringResult { Checks = [], Probe = null });
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
            ReportAggregator.ToMeasurements(task) with { HealthSignalCounts = new Dictionary<string, long> { ["warning"] = 2 } }, [], 0, null,
            new AnswerScoringResult { Checks = [], Probe = null });
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
            ReportAggregator.ToMeasurements(task) with { HealthSignalCounts = new Dictionary<string, long> { ["warning"] = 2 } }, [], 0, null,
            new AnswerScoringResult { Checks = [], Probe = null });
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
            ReportAggregator.ToMeasurements(task) with { ProviderCalls = [task.ProviderCalls[0] with { InputTokens = null }] }, [], 0, null,
            new AnswerScoringResult { Checks = [], Probe = null });
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
    ///     Verifies that schema version 3 reports with session directories and artifact records compare successfully.
    /// </summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task RunAsync_VersionThreeReportsWithSessionFields_ComparesSuccessfully()
    {
        var result = await CompareAsync(WithSession(ReportFixture.Create(inputTokens: 100)), WithSession(ReportFixture.Create(inputTokens: 130)));

        result.ExitCode.Should().Be(0);
        result.Text.Should().Contain("providerInputTokens: baseline=100 candidate=130 delta=30");
    }

    /// <summary>
    ///     Verifies that a report with another schema version is rejected.
    /// </summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task RunAsync_SchemaVersionOneReport_FailsValidation()
    {
        var result = await CompareAsync(ReportFixture.Create() with { SchemaVersion = 1 }, ReportFixture.Create());

        result.ExitCode.Should().Be(1);
        result.Text.Should().Contain("Baseline: Unsupported report schema version; expected 3.");
    }

    /// <summary>
    ///     Verifies that a report in the schema version 1 shape, which records a session log path, is rejected.
    /// </summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task RunAsync_ReportInVersionOneShape_ReturnsFailure()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tg-compare-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var currentPath = Path.Combine(directory, "current.json");
            var legacyPath = Path.Combine(directory, "legacy.json");
            var json = JsonSerializer.Serialize(ReportFixture.Create(), ReportJson.Options);
            await File.WriteAllTextAsync(currentPath, json);
            await File.WriteAllTextAsync(legacyPath, json.Replace("\"sessionDirectory\"", "\"sessionLogPath\"", StringComparison.Ordinal));
            using var output = new StringWriter();

            var exit = await ComparisonCommand.RunAsync([legacyPath, currentPath], output);

            exit.Should().Be(1);
            output.ToString().Should().Contain("Malformed report JSON, unsupported schema, or invalid required report shape.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
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

    /// <summary>Verifies new rate/count limits reject regressions, accept equality and accept improvements.</summary>
    /// <param name="metric">The metric under test.</param>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Theory]
    [InlineData("checkPassRate")]
    [InlineData("deliverableCompletionRate")]
    [InlineData("probePassRate")]
    [InlineData("invalidProbeCount")]
    [InlineData("messagesMasked")]
    [InlineData("messagesSummarized")]
    public async Task RunAsync_QualityLimitsUseSpecifiedDirection(string metric)
    {
        var good = QualityReport(true);
        var bad = QualityReport(false);
        if (metric == "invalidProbeCount")
        {
            bad = ReportFixture.Scored(bad.Tasks[0].Checks,
                new ProbeResult { Requires = null, Status = "invalid", Reason = "canaryRepeated", CanaryPresent = true });
        }
        if (metric is "messagesMasked" or "messagesSummarized")
        {
            var task = good.Tasks[0];
            var measurements = ReportAggregator.ToMeasurements(task) with { MessagesMasked = 10, MessagesSummarized = 11 };
            var aggregator = new ReportAggregator();
            var changed = aggregator.CreateTask(task.TaskId, task.Size, task.Outcome, task.ProtocolCompletion, task.ModelCallBudget,
                measurements, [], 0, null, new AnswerScoringResult { Checks = task.Checks, Probe = task.Probe });
            bad = aggregator.CreateReport(good.Run, [changed], [], false);
        }
        var options = new[] { "--limit", metric + "=0" };
        (await CompareAsync(good, bad, options)).ExitCode.Should().Be(1);
        (await CompareAsync(good, good, options)).ExitCode.Should().Be(0);
        (await CompareAsync(bad, good, options)).ExitCode.Should().Be(0);
        (await CompareAsync(good, bad, ["--limit", metric + "=100"])).ExitCode.Should().Be(0);
    }

    /// <summary>Verifies quality populations remain informational even when available.</summary>
    /// <param name="metric">The excluded metric.</param>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Theory]
    [InlineData("checksTotal")]
    [InlineData("checksPassed")]
    [InlineData("evaluatedTaskCount")]
    [InlineData("deliverableCompletedTaskCount")]
    [InlineData("probeCount")]
    [InlineData("passedProbeCount")]
    [InlineData("canaryPresentCount")]
    public async Task RunAsync_RejectsInformationalQualityLimits(string metric)
    {
        var report = QualityReport(true);
        var result = await CompareAsync(report, report, ["--limit", metric + "=0"]);
        result.ExitCode.Should().Be(1);
        result.Text.Should().Contain("informational metric");
    }

    /// <summary>Verifies unavailable rates fail a configured limit.</summary>
    /// <param name="metric">The unavailable quality rate.</param>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Theory]
    [InlineData("checkPassRate")]
    [InlineData("deliverableCompletionRate")]
    [InlineData("probePassRate")]
    public async Task RunAsync_UnavailableQualityRateFailsLimit(string metric)
    {
        var report = ReportFixture.Create();
        var result = await CompareAsync(report, report, ["--limit", metric + "=0"]);
        result.ExitCode.Should().Be(1);
        result.Text.Should().Contain("metric is unavailable: " + metric);
    }

    /// <summary>Verifies changed checks/probes and failed-side counts appear in comparison output.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task RunAsync_PrintsQualityVerdictsAndCounters()
    {
        var result = await CompareAsync(QualityReport(true), QualityReport(false));
        result.ExitCode.Should().Be(0);
        result.Text.Should().Contain("task task check fact: baseline=pass candidate=fail(notFound)");
        result.Text.Should().Contain("candidate=failed(canaryMissing) CanaryPresent=False");
        result.Text.Should().Contain("failed candidate probe: masked=2 summarized=3 dropped=4");
        result.Text.Should().Contain("totals evaluatedTaskCount:").And.Contain("totals canaryPresentCount:");
        var baseline = QualityReport(false);
        var candidate = ReportFixture.Scored([baseline.Tasks[0].Checks[0] with { Reason = "forbiddenValuePresent" }], baseline.Tasks[0].Probe);
        (await CompareAsync(baseline, candidate)).Text.Should().Contain("baseline=fail(notFound) candidate=fail(forbiddenValuePresent)");
    }

    /// <summary>Verifies missing checks/probes and different required kinds require the compatibility override.</summary>
    /// <param name="change">The declaration change.</param>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Theory]
    [InlineData("check")]
    [InlineData("probe")]
    [InlineData("requires")]
    public async Task RunAsync_DeclarationChangesRequireOverride(string change)
    {
        var baseline = QualityReport(true);
        var candidate = ReportFixture.Scored(change == "check" ? [] : baseline.Tasks[0].Checks,
            change == "probe" ? null : baseline.Tasks[0].Probe! with { Requires = change == "requires" ? "masked" : null });
        (await CompareAsync(baseline, candidate)).ExitCode.Should().Be(1);
        var overridden = await CompareAsync(baseline, candidate, ["--allow-incompatible"]);
        overridden.ExitCode.Should().Be(0);
        overridden.Text.Should().Contain("Compatibility override enabled");
        if (change == "check")
            overridden.Text.Should().Contain("baseline=pass candidate=missing");
        if (change == "probe")
            overridden.Text.Should().Contain("candidate=none");
    }

    /// <summary>Verifies help documents directions and exclusions.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task RunAsync_HelpExplainsQualityLimitContract()
    {
        using var output = new StringWriter();
        (await ComparisonCommand.RunAsync(["--help"], output)).Should().Be(0);
        output.ToString().Should().Contain("Decreases regress checkPassRate, deliverableCompletionRate, probePassRate");
        output.ToString().Should().Contain("increases regress invalidProbeCount, messagesMasked, messagesSummarized");
        output.ToString().Should().Contain("Informational exclusions: checksTotal, checksPassed, evaluatedTaskCount");
    }

    private static RunReport QualityReport(bool passed) => ReportFixture.Scored(
        [new CheckResult { Id = "fact", Passed = passed, Reason = passed ? null : "notFound" }],
        new ProbeResult { Requires = null, Status = passed ? "passed" : "failed", Reason = passed ? null : "canaryMissing", CanaryPresent = passed });

    /// <summary>Verifies schema-3 check/probe fields are required and unknown/null/duplicate data is rejected.</summary>
    /// <param name="corruption">The independent JSON corruption.</param>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Theory]
    [InlineData("missingChecks")]
    [InlineData("missingProbe")]
    [InlineData("nullChecks")]
    [InlineData("nullCheck")]
    [InlineData("missingReason")]
    [InlineData("duplicateStatus")]
    [InlineData("unknownField")]
    public async Task RunAsync_RejectsMalformedQualityShape(string corruption)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(QualityReport(true), ReportJson.Options))!;
        var task = node["tasks"]![0]!.AsObject();
        switch (corruption)
        {
            case "missingChecks":
                task.Remove("checks");
                break;
            case "missingProbe":
                task.Remove("probe");
                break;
            case "nullChecks":
                task["checks"] = null;
                break;
            case "nullCheck":
                task["checks"]![0] = null;
                break;
            case "missingReason":
                task["checks"]![0]!.AsObject().Remove("reason");
                break;
            case "unknownField":
                task["probe"]!["canary"] = "SHOULD-NOT-BE-IN-REPORT";
                break;
        }
        var json = node.ToJsonString();
        if (corruption == "duplicateStatus")
            json = json.Replace("\"status\":\"passed\"", "\"status\":\"passed\",\"status\":\"passed\"", StringComparison.Ordinal);
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, json);
            using var output = new StringWriter();
            (await ComparisonCommand.RunAsync([path, path], output)).Should().Be(1);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static RunReport WithSession(RunReport report)
    {
        var task = report.Tasks[0];
        var aggregator = new ReportAggregator();
        var measured = aggregator.CreateTask(task.TaskId, task.Size, task.Outcome, task.ProtocolCompletion, task.ModelCallBudget,
            ReportAggregator.ToMeasurements(task), [], 0,
            new TaskSessionRecord("session", task.TaskId, [], [new ArtifactFileReport { Path = "report.md", SizeBytes = 12 }]),
            new AnswerScoringResult { Checks = [], Probe = null });
        return aggregator.CreateReport(report.Run, [measured], [], false);
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
