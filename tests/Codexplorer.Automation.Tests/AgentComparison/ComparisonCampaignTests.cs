using System.Text.Json.Nodes;
using Codexplorer.Automation.AgentComparison;
using Codexplorer.Automation.AgentComparison.Recording;
using Codexplorer.Automation.Configuration;
using FluentAssertions;

namespace Codexplorer.Automation.Tests.AgentComparison;

/// <summary>Verifies partial-session persistence and standalone report inspection.</summary>
public sealed class ComparisonCampaignTests
{
    /// <summary>Keeps cancelled staging observable without starting an inference endpoint or runtime.</summary>
    [Fact]
    public async Task Execute_CancelledStaging_PersistsPartialOutcomeAndUnknownUsage()
    {
        var root = Path.Combine(Path.GetTempPath(), "comparison-cancelled-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var failure = new RuntimeResult("cancelled", true, null, 0, [], "OperationCanceledException");
            var fake = new FakeOpenRouterProvider([]);
            var report = await ComparisonCampaign.ExecuteAsync(Session(), "does-not-exist", root, Settings(), "synthetic",
                fake, null, cancellation.Token, failure);
            report.Session.Runtime.Outcome.Should().Be("cancelled");
            report.Session.Runtime.Partial.Should().BeTrue();
            report.Attempts.Should().BeEmpty();
            report.Usage.InputTokens.Should().BeNull();
            report.LiveExecutionStarted.Should().BeFalse();
            fake.Requests.Should().BeEmpty();
            File.Exists(Path.Combine(root, "report.json")).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Displays partial outcomes and refuses tampered totals while making no model requests.</summary>
    [Fact]
    public async Task Compare_SyntheticPartialAndTamperedTotals_PreservesUnknownsAndRejectsTampering()
    {
        var root = Path.Combine(Path.GetTempPath(), "comparison-report-" + Guid.NewGuid().ToString("N"));
        try
        {
            var failure = new RuntimeResult("failed", true, null, 0, [], "StagingFailure");
            await ComparisonCampaign.ExecuteAsync(Session(), "does-not-exist", root, Settings(), "synthetic",
                new FakeOpenRouterProvider([]), null, CancellationToken.None, failure);
            using var output = new StringWriter();
            var code = await ComparisonReportCommand.RunAsync(root, "codexplorer-control", output, CancellationToken.None);
            code.Should().Be(0);
            output.ToString().Should().Contain("failed (partial)").And.Contain("INCOMPLETE ACCOUNTING")
                .And.Contain("unknown").And.Contain("no performance conclusion");
            var reportPath = Path.Combine(root, "report.json");
            var report = JsonNode.Parse(await File.ReadAllTextAsync(reportPath))!;
            report["usage"]!["inputTokens"] = 42;
            await File.WriteAllTextAsync(reportPath, report.ToJsonString());
            var action = () => ComparisonReportCommand.RunAsync(root, null, TextWriter.Null, CancellationToken.None);
            await action.Should().ThrowAsync<InvalidDataException>().WithMessage("*totals*");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Retains partial reports when artifact collection rejects an escaping link.</summary>
    [Fact]
    public async Task Execute_ArtifactRootIsLinked_PersistsFailureWithoutReadingTheTarget()
    {
        var root = Path.Combine(Path.GetTempPath(), "comparison-linked-" + Guid.NewGuid().ToString("N"));
        var outside = Path.Combine(root, "outside");
        Directory.CreateDirectory(outside);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(outside, "evidence.md"), "private sentinel");
            var sessionRoot = Path.Combine(root, "session");
            Directory.CreateDirectory(sessionRoot);
            Directory.CreateSymbolicLink(Path.Combine(sessionRoot, "artifacts"), outside);
            var report = await ComparisonCampaign.ExecuteAsync(Session(), "does-not-exist", sessionRoot, Settings(), "synthetic",
                new FakeOpenRouterProvider([]), null, CancellationToken.None,
                new RuntimeResult("cancelled", true, null, 0, [], "staging"));
            report.Session.Runtime.Outcome.Should().Be("failed");
            report.Session.Runtime.Partial.Should().BeTrue();
            report.Session.Artifacts.Should().BeEmpty();
            report.Session.EvidenceChecks.Should().OnlyContain(check => !check.Passed);
            (await File.ReadAllTextAsync(Path.Combine(sessionRoot, "report.json"))).Should().NotContain("private sentinel");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static ComparisonSettings Settings() => new("qwen/qwen3.7-flash", "alibaba", 32768, true);

    private static ComparisonSessionPlan Session() => new("partial", "synthetic", 1, "opencode",
        new AutomationTaskDefinition
        {
            TaskId = "partial", Checks =
            [
                new AutomationCheckDefinition { Id = "evidence", Kind = "contains", Artifact = "evidence.md", AnyOf = ["fact"] },
                new AutomationCheckDefinition { Id = "answer", Kind = "contains", AnyOf = ["fact"] }
            ]
        }, "prompt", "source-hash", "adapted-hash", 3, 1, null);
}
