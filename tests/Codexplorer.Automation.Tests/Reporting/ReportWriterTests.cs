using System.Text.Json;
using Codexplorer.Automation.Reporting;
using FluentAssertions;

namespace Codexplorer.Automation.Tests.Reporting;

/// <summary>
///     Represents deterministic coverage of durable report writing.
/// </summary>
/// <remarks>Tests exercise public behavior through deterministic measurement fixtures.</remarks>
public sealed class ReportWriterTests
{
    /// <summary>
    ///     Verifies that replaces report and writes only allowlisted metadata and measurements.
    /// </summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task WriteAsync_ReplacesReportAndWritesOnlyAllowlistedMetadataAndMeasurements()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tg-report-" + Guid.NewGuid().ToString("N"));
        try
        {
            var writer = new JsonRunReportWriter();
            await writer.WriteAsync(ReportFixture.Create(inputTokens: 100), directory);
            await writer.WriteAsync(ReportFixture.Create(inputTokens: 200), directory);

            var json = await File.ReadAllTextAsync(Path.Combine(directory, "run-report.json"));
            var report = JsonSerializer.Deserialize<RunReport>(json, ReportJson.Options)!;
            report.Totals.Metrics.ProviderInputTokens.Should().Be(200);
            json.Should().Contain("\"schemaVersion\": 4");
            json.Should().NotContain("initialPrompt").And.NotContain("apiKey").And.NotContain("repositoryUrl").And.NotContain("answerText");
            Directory.GetFiles(directory).Should().HaveCount(1);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
    /// <summary>Verifies schema-3 verdict writing excludes scoring inputs while retaining nullable fields.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task WriteAsync_QualityResultsExcludeExpectedValuesPatternsAndCodes()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tg-scored-report-" + Guid.NewGuid().ToString("N"));
        try
        {
            var report = ReportFixture.Scored([new CheckResult { Id = "fact", Passed = true, Reason = null }],
                new ProbeResult { Requires = null, Status = "passed", Reason = null, CanaryPresent = true });
            await new JsonRunReportWriter().WriteAsync(report, directory);
            var json = await File.ReadAllTextAsync(Path.Combine(directory, "run-report.json"));
            json.Should().Contain("\"checks\":").And.Contain("\"canaryPresent\": true").And.Contain("\"reason\": null");
            json.Should().NotContain("\"anyOf\"").And.NotContain("\"noneOf\"").And.NotContain("\"pattern\"")
                .And.NotContain("\"canary\"").And.NotContain("\"finalAnswer\"");
            var written = JsonSerializer.Deserialize<RunReport>(json, ReportJson.Options)!;
            ReportValidator.Validate(written).Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

}
