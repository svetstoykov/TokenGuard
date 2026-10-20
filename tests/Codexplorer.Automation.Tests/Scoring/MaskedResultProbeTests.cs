using System.Text.Json;
using Codexplorer.Automation.Configuration;
using Codexplorer.Automation.Reporting;
using Codexplorer.Measurements;
using Codexplorer.Automation.Scoring;
using Codexplorer.Automation.Tests.Reporting;
using FluentAssertions;

namespace Codexplorer.Automation.Tests.Scoring;

/// <summary>Verifies masked-result scoring and capture-independent eligibility validation.</summary>
public sealed class MaskedResultProbeTests
{
    /// <summary>Verifies every verdict and invalidity reason against independent prepare evidence.</summary>
    /// <param name="state">The carrier state at the question.</param>
    /// <param name="elsewhere">Whether the fact has another copy.</param>
    /// <param name="masked">The task masking count.</param>
    /// <param name="answer">The final answer, or null for no answer.</param>
    /// <param name="status">The expected verdict.</param>
    /// <param name="reason">The expected reason.</param>
    [Theory]
    [InlineData("masked", false, 1, "**TG-UNIQUE**", "passed", null)]
    [InlineData("masked", false, 1, "tg-alias", "passed", null)]
    [InlineData("masked", false, 1, "unknown", "failed", "factMissing")]
    [InlineData("visible", true, 0, "TG-UNIQUE", "notApplicable", "noMasking")]
    [InlineData("unseen", true, 1, "TG-UNIQUE", "invalid", "factNotRead")]
    [InlineData("visible", true, 1, "TG-UNIQUE", "invalid", "resultNotMasked")]
    [InlineData("removed", true, 1, "TG-UNIQUE", "invalid", "resultRemoved")]
    [InlineData("masked", true, 1, "TG-UNIQUE", "invalid", "factVisibleElsewhere")]
    [InlineData("masked", false, 0, null, "invalid", "noAnswer")]
    public void Score_UsesQuestionEvidenceAndReasonPrecedence(string state, bool elsewhere, long masked, string? answer,
        string status, string? reason)
    {
        var report = CreateReport(state, elsewhere, masked, answer);
        var probe = report.Tasks.Single().Probe!;

        probe.Status.Should().Be(status);
        probe.Reason.Should().Be(reason);
        probe.Kind.Should().Be("maskedResult");
        report.Run.CaptureEnabled.Should().BeFalse();
        ReportValidator.Validate(report).Should().BeEmpty();
        report.Totals.ProbeCount.Should().Be(0);
        report.Totals.MaskedResultProbes!.ProbeCount.Should().Be(1);
        report.Totals.MaskedResultProbes.ProbePassRate.Should().Be(status == "passed" ? 1 : status == "failed" ? 0 : null);
    }

    /// <summary>Verifies a protocol-complete exchange without usable final answer text keeps noAnswer precedence.</summary>
    [Fact]
    public void Validate_CompleteProtocolWithoutAnswer_AcceptsNoAnswerVerdict()
    {
        var original = CreateReport("masked", false, 1, null);
        var task = original.Tasks.Single() with { ProtocolCompletion = true, Outcome = "reply_received" };
        var report = new ReportAggregator().CreateReport(original.Run, [task], [], false);

        ReportValidator.Validate(report).Should().BeEmpty();
    }

    /// <summary>Verifies a refetch can restore visibility without changing the original eligibility.</summary>
    [Fact]
    public void Score_RefetchedAfterQuestion_PreservesEligiblePass()
    {
        var report = CreateReport("masked", false, 1, "TG-UNIQUE", refetched: true);

        report.Tasks.Single().Probe.Should().BeEquivalentTo(new
        {
            Kind = "maskedResult", Status = "passed", Refetched = true, CanaryPresent = true
        });
        report.Totals.MaskedResultProbes!.RefetchedCount.Should().Be(1);
        ReportValidator.Validate(report).Should().BeEmpty();
    }

    /// <summary>Verifies the two probe kinds have independent pass-rate denominators in a mixed run.</summary>
    [Fact]
    public void Aggregate_MixedProbeKinds_KeepsRetentionPopulationSeparate()
    {
        var masked = CreateReport("masked", false, 1, "unknown");
        var retention = ReportFixture.Scored([], new ProbeResult
        {
            Requires = null, Status = "passed", Reason = null, CanaryPresent = true
        }).Tasks.Single() with { TaskId = "retention" };

        var combined = new ReportAggregator().CreateReport(masked.Run, [masked.Tasks.Single(), retention], [], false);

        combined.Totals.ProbeCount.Should().Be(1);
        combined.Totals.ProbePassRate.Should().Be(1);
        combined.Totals.MaskedResultProbes!.ProbeCount.Should().Be(1);
        combined.Totals.MaskedResultProbes.ProbePassRate.Should().Be(0);
        ReportValidator.Validate(combined).Should().BeEmpty();
    }

    /// <summary>Verifies report validation independently detects tampered evidence and totals.</summary>
    /// <param name="corruption">The independent report corruption.</param>
    [Theory]
    [InlineData("state")]
    [InlineData("contamination")]
    [InlineData("missing")]
    [InlineData("boundary")]
    [InlineData("refetch")]
    [InlineData("totals")]
    [InlineData("unknownState")]
    public void Validate_CorruptedEvidence_RejectsPassedProbe(string corruption)
    {
        var report = CreateReport("masked", false, 1, "TG-UNIQUE");
        var task = report.Tasks.Single();
        var prepare = task.PrepareRecords.Single();
        prepare = corruption switch
        {
            "state" => prepare with { ProbeValueState = "removed" },
            "contamination" => prepare with { ProbeFactVisibleElsewhere = true },
            "missing" => prepare with { ProbeFactVisibleElsewhere = null },
            "boundary" => prepare with { ProbeQuestionAsked = false },
            "refetch" => prepare with { ProbeRefetched = true },
            "unknownState" => prepare with { ProbeValueState = "other" },
            _ => prepare
        };
        report = report with { Tasks = [task with { PrepareRecords = [prepare] }] };
        if (corruption == "totals")
            report = report with { Totals = report.Totals with { MaskedResultProbes = report.Totals.MaskedResultProbes! with { ProbeCount = 2 } } };

        ReportValidator.Validate(report).Should().NotBeEmpty();
    }

    /// <summary>Verifies serialized schema 7 reports preserve capture-independent evidence.</summary>
    [Fact]
    public void Report_RoundTrip_RechecksEligibilityWithCaptureOff()
    {
        var report = CreateReport("masked", false, 1, "TG-UNIQUE", refetched: true);
        var json = JsonSerializer.Serialize(report, ReportJson.Options);

        var restored = JsonSerializer.Deserialize<RunReport>(json, ReportJson.Options)!;

        ReportValidator.Validate(restored).Should().BeEmpty();
        restored.Tasks.Single().PrepareRecords.First().ProbeValueState.Should().Be("masked");
    }

    /// <summary>Creates a report through the scorer and aggregator with a separately recorded question boundary.</summary>
    /// <param name="state">The original carrier state.</param>
    /// <param name="elsewhere">Whether a contaminating copy exists.</param>
    /// <param name="masked">The masking count.</param>
    /// <param name="answer">The answer, or null when protocol completion failed.</param>
    /// <param name="refetched">Whether a later tool result returns the fact.</param>
    /// <returns>The scored report with capture disabled.</returns>
    internal static RunReport CreateReport(string state, bool elsewhere, long masked, string? answer, bool refetched = false)
    {
        var template = ReportFixture.Create();
        var prepare = template.Tasks[0].PrepareRecords[0] with
        {
            ProbeValueState = state, ProbeFactVisibleElsewhere = elsewhere, ProbeQuestionAsked = true, ProbeRefetched = false
        };
        var measurements = ReportAggregator.ToMeasurements(template.Tasks[0]) with
        {
            MessagesMasked = masked,
            PrepareRecords = refetched ? [prepare, prepare with { Index = 2, ProbeValueState = "visible", ProbeRefetched = true }] : [prepare]
        };
        var probe = new AutomationProbeDefinition { Kind = "maskedResult", Question = "What was the value?", AnyOf = ["TG-UNIQUE", "TG-ALIAS"] };
        var scoring = new AnswerScorer().Score([], probe, new ScoringInput
        {
            FinalAnswer = answer, ArtifactTexts = new Dictionary<string, string>(), Measurements = measurements
        });
        var aggregator = new ReportAggregator();
        var task = aggregator.CreateTask("task", "small", answer is null ? "failed" : "reply_received", answer is not null,
            24, measurements, [], 0, null, scoring);
        return aggregator.CreateReport(template.Run with { CaptureEnabled = false }, [task], [], false);
    }
}
