using Codexplorer.Automation.AgentComparison;
using Codexplorer.Automation.AgentComparison.Recording;
using FluentAssertions;

namespace Codexplorer.Automation.Tests.AgentComparison;

/// <summary>Verifies honest spend summaries across successful and failed attempts.</summary>
public sealed class ComparisonAccountingTests
{
    /// <summary>Includes failed attempts in known spend and preserves complete totals as unknown.</summary>
    [Fact]
    public void Aggregate_FailedUnknownAttempt_RetainsKnownSpendAndIncompleteTotals()
    {
        var known = new RecordedUsage(100, 20, 60, 8, null, 0.12m, true);
        var report = ComparisonAccounting.Aggregate([Attempt(1, known), Attempt(2, null, 502)]);
        report.InputTokens.Should().BeNull();
        report.OutputTokens.Should().BeNull();
        report.Cost.Should().BeNull();
        report.KnownCost.Should().Be(0.12m);
        report.CompleteTokens.Should().BeFalse();
        report.CompleteCost.Should().BeFalse();
        report.FailedAttempts.Should().Be(1);
        report.UnknownUsageAttempts.Should().Be(1);
    }

    /// <summary>Keeps cached and reasoning subset values separate from the chargeable totals.</summary>
    [Fact]
    public void Aggregate_CompleteSubsets_DoesNotDoubleCountAndIncludesFailedCost()
    {
        var usage = new RecordedUsage(100, 20, 60, 8, 5, 0.1m, true);
        var report = ComparisonAccounting.Aggregate([Attempt(1, usage), Attempt(2, usage, 500)]);
        report.InputTokens.Should().Be(200);
        report.OutputTokens.Should().Be(40);
        report.CachedInputTokens.Should().Be(120);
        report.ReasoningOutputTokens.Should().Be(16);
        report.Cost.Should().Be(0.2m);
        report.FailedAttempts.Should().Be(1);
        report.UnclassifiedCalls.Should().Be(2);
    }

    private static RecordedAttempt Attempt(long sequence, RecordedUsage? usage, int status = 200) =>
        new(sequence, "session", RecordedRequestKind.Unknown, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            [], [], [], status, "model", $"generation-{sequence}", usage, null, status >= 400 ? "failed" : null, false, null);
}
