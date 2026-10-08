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
        var measured = aggregator.CreateTask("task", "small", "reply_received", true, 24, snapshot, [], 0, null);
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
        var changed = aggregator.CreateTask("task", "small", "reply_received", true, 24, measurements, [], 0, null);

        var result = aggregator.CreateReport(report.Run, [changed], [], false);

        result.Validation.IsValid.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that only schema version 2 is accepted.
    /// </summary>
    /// <param name="schemaVersion">The unsupported schema version.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void Validate_SchemaVersionOtherThanTwo_ReturnsUnsupportedVersionError(int schemaVersion)
    {
        var report = ReportFixture.Create() with { SchemaVersion = schemaVersion };

        var errors = ReportValidator.Validate(report);

        errors.Should().ContainSingle().Which.Should().Contain("expected 2");
    }

    /// <summary>
    ///     Verifies that a schema version 2 report with a relative session directory and artifact records is valid.
    /// </summary>
    [Fact]
    public void Validate_VersionTwoReportWithRelativeSessionPaths_ReturnsNoErrors()
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

    private static RunReport WithSession(TaskSessionRecord session)
    {
        var report = ReportFixture.Create();
        var task = report.Tasks[0];
        var aggregator = new ReportAggregator();
        var measured = aggregator.CreateTask(task.TaskId, task.Size, task.Outcome, task.ProtocolCompletion, task.ModelCallBudget,
            ReportAggregator.ToMeasurements(task), [], 0, session);
        return aggregator.CreateReport(report.Run, [measured], [], false);
    }
}
