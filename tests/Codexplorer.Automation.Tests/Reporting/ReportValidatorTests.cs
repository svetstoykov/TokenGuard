using Codexplorer.Automation.Scoring;
using Codexplorer.Automation.Reporting;
using Codexplorer.Measurements;
using FluentAssertions;

namespace Codexplorer.Automation.Tests.Reporting;

/// <summary>
///     Represents deterministic coverage of report validation.
/// </summary>
/// <remarks>Tests exercise public behavior through deterministic measurement fixtures.</remarks>
public sealed class ReportValidatorTests
{
    /// <summary>
    ///     Verifies that distribution order does not change measurement meaning.
    /// </summary>
    [Fact]
    public void Validate_DistributionOrderDoesNotChangeMeasurementMeaning()
    {
        var report = ReportFixture.Create();
        var task = report.Tasks[0];
        var snapshot = ReportAggregator.ToMeasurements(task) with
        {
            HealthSignalCounts = new Dictionary<string, long> { ["first"] = 1, ["second"] = 2 }
        };
        var aggregator = new ReportAggregator();
        var measured = aggregator.CreateTask(
            "task", "small", "reply_received", true, 24, snapshot, [], 0, null,
            new AnswerScoringResult { Checks = [], Probe = null });
        var ordered = aggregator.CreateReport(report.Run, [measured], [], false);
        var reversed = new Dictionary<string, long> { ["second"] = 2, ["first"] = 1 };
        var changed = ordered with
        {
            Tasks = [measured with { Metrics = measured.Metrics with { HealthSignalCounts = reversed } }],
            Totals = ordered.Totals with { Metrics = ordered.Totals.Metrics with { HealthSignalCounts = reversed } }
        };

        ReportValidator.Validate(changed).Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies that nonfinite effective threshold is invalid.
    /// </summary>
    [Fact]
    public void CreateReport_NonfiniteEffectiveThresholdIsInvalid()
    {
        var report = ReportFixture.Create();
        var metadata = report.Run with { EffectiveSettings = report.Run.EffectiveSettings with { SoftThresholdRatio = double.NaN } };

        var result = new ReportAggregator().CreateReport(metadata, report.Tasks, [], false);

        result.Validation.IsValid.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that partial control is invalid even with complete measurement snapshots.
    /// </summary>
    [Fact]
    public void Validate_PartialControlIsInvalidEvenWithCompleteMeasurementSnapshots()
    {
        var report = ReportFixture.Create("control") with { Partial = true };

        var errors = ReportValidator.Validate(report);

        errors.Should().Contain(error => error.Contains("Control", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Verifies that nonfinite totals return validation errors.
    /// </summary>
    [Fact]
    public void Validate_NonfiniteTotalsReturnValidationErrors()
    {
        var report = ReportFixture.Create();
        report = report with { Totals = report.Totals with { ProtocolCompletionRate = double.PositiveInfinity } };

        var errors = ReportValidator.Validate(report);

        errors.Should().Contain(error => error.Contains("nonfinite", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Verifies that control with incomplete or non ready prepare is invalid.
    /// </summary>
    /// <param name="status">The completed or incomplete prepare status.</param>
    /// <param name="outcome">The terminal protocol outcome.</param>
    [Theory]
    [InlineData("incomplete", null)]
    [InlineData("completed", "Compacted")]
    public void CreateReport_ControlWithIncompleteOrNonReadyPrepareIsInvalid(string status, string? outcome)
    {
        var report = ReportFixture.Create("control");
        var task = report.Tasks[0];
        var measurements = ReportAggregator.ToMeasurements(task) with
        {
            PrepareRecords = [task.PrepareRecords[0] with
            {
                Status = status, Outcome = outcome, TokensBefore = status == "completed" ? 100 : null,
                TokensAfter = status == "completed" ? 80 : null
            }]
        };
        var aggregator = new ReportAggregator();
        var changed = aggregator.CreateTask(
            "task", "small", "reply_received", true, 24, measurements, [], 0, null,
            new AnswerScoringResult { Checks = [], Probe = null });

        var result = aggregator.CreateReport(report.Run, [changed], [], false);

        result.Validation.IsValid.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that only schema version 3 is accepted.
    /// </summary>
    /// <param name="schemaVersion">The unsupported schema version.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Validate_SchemaVersionOtherThanThree_ReturnsUnsupportedVersionError(int schemaVersion)
    {
        var report = ReportFixture.Create() with { SchemaVersion = schemaVersion };

        var errors = ReportValidator.Validate(report);

        errors.Should().ContainSingle().Which.Should().Contain("expected 3");
    }

    /// <summary>
    ///     Verifies that a schema version 3 report with a relative session directory and artifact records is valid.
    /// </summary>
    [Fact]
    public void Validate_VersionThreeReportWithRelativeSessionPaths_ReturnsNoErrors()
    {
        var report = WithSession(new TaskSessionRecord("session", "task", [], [new ArtifactFileReport { Path = "report.md", SizeBytes = 12 }]));

        var errors = ReportValidator.Validate(report);

        errors.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies that an absolute session directory or artifact path is invalid.
    /// </summary>
    /// <param name="sessionDirectory">The recorded session directory.</param>
    /// <param name="artifactPath">The recorded artifact path.</param>
    [Theory]
    [InlineData("/home/user/run/task", "report.md")]
    [InlineData("C:\\runs\\task", "report.md")]
    [InlineData("task", "/home/user/run/task/artifacts/report.md")]
    public void Validate_AbsoluteSessionOrArtifactPath_ReturnsAbsolutePathError(string sessionDirectory, string artifactPath)
    {
        var report = WithSession(
            new TaskSessionRecord("session", sessionDirectory, [], [new ArtifactFileReport { Path = artifactPath, SizeBytes = 12 }]));

        var errors = ReportValidator.Validate(report);

        errors.Should().Contain(error => error.Contains("absolute session path", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Verifies that an absolute manifest path is invalid.
    /// </summary>
    [Fact]
    public void CreateReport_AbsoluteManifestPath_IsInvalid()
    {
        var report = ReportFixture.Create();
        var metadata = report.Run with { ManifestPath = "/home/user/tasks/test.json" };

        var result = new ReportAggregator().CreateReport(metadata, report.Tasks, [], false);

        result.Validation.Errors.Should().Contain(error => error.Contains("manifest path", StringComparison.Ordinal));
    }

    /// <summary>Verifies invalid check/result structures and derived metrics are rejected.</summary>
    /// <param name="corruption">The independently applied corruption.</param>
    [Theory]
    [InlineData("nullChecks")]
    [InlineData("nullEntry")]
    [InlineData("badId")]
    [InlineData("duplicate")]
    [InlineData("passReason")]
    [InlineData("failReason")]
    [InlineData("completion")]
    [InlineData("total")]
    [InlineData("rate")]
    [InlineData("masked")]
    [InlineData("summarized")]
    [InlineData("prepareNull")]
    [InlineData("incompleteFlag")]
    public void Validate_RejectsQualityCorruption(string corruption)
    {
        var report = ReportFixture.Scored([new CheckResult { Id = "fact", Passed = true, Reason = null }]);
        var task = report.Tasks.Single();
        var check = task.Checks.Single();
        task = corruption switch
        {
            "nullChecks" => task with { Checks = null! },
            "nullEntry" => task with { Checks = [null!] },
            "badId" => task with { Checks = [check with { Id = "_bad" }] },
            "duplicate" => task with { Checks = [check, check with { Id = "FACT" }] },
            "passReason" => task with { Checks = [check with { Reason = "notFound" }] },
            "failReason" => task with { Checks = [check with { Passed = false, Reason = null }] },
            "completion" => task with { DeliverableCompletion = "notEvaluated" },
            "total" => task with { Metrics = task.Metrics with { ChecksTotal = 2 } },
            "rate" => task with { Metrics = task.Metrics with { CheckPassRate = double.NaN } },
            "masked" => task with { Metrics = task.Metrics with { MessagesMasked = -1 } },
            "summarized" => task with { Metrics = task.Metrics with { MessagesSummarized = -1 } },
            "prepareNull" => task with { PrepareRecords = [task.PrepareRecords[0] with { OpeningMessagePresent = null }] },
            _ => task with { PrepareRecords = [new PrepareMeasurement { Index = 1, OpeningMessagePresent = false }] },
        };
        ReportValidator.Validate(report with { Tasks = [task] }).Should().NotBeEmpty();
    }

    /// <summary>Verifies all probe reason/status constraints using independent rebuilt reports.</summary>
    /// <param name="status">The recorded status.</param>
    /// <param name="reason">The recorded reason.</param>
    /// <param name="present">The recorded code presence.</param>
    /// <param name="opening">The opening-message evidence.</param>
    /// <param name="protocol">The protocol completion.</param>
    /// <param name="valid">Whether the result is consistent.</param>
    [Theory]
    [InlineData("passed", null, true, false, true, true)]
    [InlineData("passed", null, false, false, true, false)]
    [InlineData("passed", "canaryMissing", true, false, true, false)]
    [InlineData("passed", null, true, true, true, false)]
    [InlineData("passed", null, true, false, false, false)]
    [InlineData("failed", "canaryMissing", false, false, true, true)]
    [InlineData("failed", "canaryMissing", true, false, true, false)]
    [InlineData("failed", null, false, false, true, false)]
    [InlineData("invalid", "noAnswer", false, false, true, true)]
    [InlineData("invalid", "noAnswer", false, false, false, true)]
    [InlineData("invalid", "noAnswer", true, false, true, false)]
    [InlineData("invalid", "canaryRepeated", true, true, true, true)]
    [InlineData("invalid", "canaryRepeated", false, false, true, true)]
    [InlineData("invalid", "canaryRepeated", true, true, false, false)]
    [InlineData("invalid", "instructionNotCompacted", true, true, true, true)]
    [InlineData("invalid", "instructionNotCompacted", true, false, true, false)]
    [InlineData("invalid", "requiredKindAbsent", false, true, true, false)]
    [InlineData("unknown", null, false, false, true, false)]
    public void Validate_ProbeVerdictsFollowEvidence(string status, string? reason, bool present, bool opening, bool protocol, bool valid)
    {
        var report = ReportFixture.Scored([], new ProbeResult { Requires = null, Status = status, Reason = reason, CanaryPresent = present },
            opening, protocol);
        ReportValidator.Validate(report).Any().Should().Be(!valid);
    }

    /// <summary>Verifies invalid probe kind and nonfinite aggregate rates are rejected.</summary>
    [Fact]
    public void Validate_RejectsUnknownRequiresAndNonfiniteTotals()
    {
        var report = ReportFixture.Scored([], new ProbeResult { Requires = "unknown", Status = "passed", Reason = null, CanaryPresent = true });
        ReportValidator.Validate(report).Should().NotBeEmpty();
        report = ReportFixture.Create();
        ReportValidator.Validate(report with { Totals = report.Totals with { ProbePassRate = double.NaN } }).Should().NotBeEmpty();
        ReportValidator.Validate(report with { Totals = report.Totals with { DeliverableCompletionRate = double.NaN } }).Should().NotBeEmpty();
    }

    /// <summary>Verifies every new aggregate count and rate is reconstructed from task results.</summary>
    /// <param name="field">The corrupted total field.</param>
    [Theory]
    [InlineData("checksTotal")]
    [InlineData("checksPassed")]
    [InlineData("checkPassRate")]
    [InlineData("evaluatedTaskCount")]
    [InlineData("deliverableCompletedTaskCount")]
    [InlineData("deliverableCompletionRate")]
    [InlineData("probeCount")]
    [InlineData("invalidProbeCount")]
    [InlineData("passedProbeCount")]
    [InlineData("probePassRate")]
    [InlineData("canaryPresentCount")]
    public void Validate_RejectsCorruptedQualityTotals(string field)
    {
        var report = ReportFixture.Scored([new CheckResult { Id = "fact", Passed = true, Reason = null }],
            new ProbeResult { Requires = null, Status = "passed", Reason = null, CanaryPresent = true });
        var totals = report.Totals;
        totals = field switch
        {
            "checksTotal" => totals with { Metrics = totals.Metrics with { ChecksTotal = 2 } },
            "checksPassed" => totals with { Metrics = totals.Metrics with { ChecksPassed = 0 } },
            "checkPassRate" => totals with { Metrics = totals.Metrics with { CheckPassRate = 0 } },
            "evaluatedTaskCount" => totals with { EvaluatedTaskCount = 0 },
            "deliverableCompletedTaskCount" => totals with { DeliverableCompletedTaskCount = 0 },
            "deliverableCompletionRate" => totals with { DeliverableCompletionRate = null },
            "probeCount" => totals with { ProbeCount = 2 },
            "invalidProbeCount" => totals with { InvalidProbeCount = 1 },
            "passedProbeCount" => totals with { PassedProbeCount = 0 },
            "probePassRate" => totals with { ProbePassRate = null },
            _ => totals with { CanaryPresentCount = 0 },
        };
        ReportValidator.Validate(report with { Totals = totals }).Should().Contain(error => error.Contains("totals are inconsistent"));
    }

    /// <summary>Verifies required-kind invalidity follows opening evidence and counters.</summary>
    /// <param name="opening">Whether the opening survived.</param>
    /// <param name="reason">The expected invalid reason.</param>
    [Theory]
    [InlineData(true, "instructionNotCompacted")]
    [InlineData(false, "requiredKindAbsent")]
    public void Validate_KindEligibilityUsesSharedPrecedence(bool opening, string reason)
    {
        var report = ReportFixture.Scored(
            [], new ProbeResult { Requires = "masked", Status = "invalid", Reason = reason, CanaryPresent = true }, opening);
        var task = report.Tasks.Single();
        task = task with { Metrics = task.Metrics with { MessagesMasked = 0 } };
        report = new ReportAggregator().CreateReport(report.Run, [task], [], false);
        ReportValidator.Validate(report).Should().BeEmpty();
        ReportValidator.Validate(report with
        {
            Tasks = [task with { Probe = task.Probe! with { Reason = opening ? "requiredKindAbsent" : "instructionNotCompacted" } }],
        }).Should().NotBeEmpty();
    }

    private static RunReport WithSession(TaskSessionRecord session)
    {
        var report = ReportFixture.Create();
        var task = report.Tasks[0];
        var aggregator = new ReportAggregator();
        var measured = aggregator.CreateTask(task.TaskId, task.Size, task.Outcome, task.ProtocolCompletion, task.ModelCallBudget,
            ReportAggregator.ToMeasurements(task), [], 0, session, new AnswerScoringResult { Checks = [], Probe = null });
        return aggregator.CreateReport(report.Run, [measured], [], false);
    }
}
