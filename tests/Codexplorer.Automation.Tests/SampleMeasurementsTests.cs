extern alias sample;

using System.ClientModel;
using System.ClientModel.Primitives;
using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using Serilog;
using TokenGuard.Core;
using TokenGuard.Core.Configuration;
using TokenGuard.Core.Options;
using TokenGuard.Extensions.OpenAI;
using sample::Codexplorer.Diagnostics;

namespace Codexplorer.Automation.Tests;


/// <summary>Verifies collection through the real telemetry and structured logging boundaries.</summary>
[Collection("Sample telemetry")]
public sealed class SampleMeasurementsTests
{
    /// <summary>Verifies final summary agreement without debug logging.</summary>
    [Fact]
    public async Task Dispose_WithInformationLogging_CrossChecksTheRealSummary()
    {
        using var fixture = new SampleTelemetryFixture();
        fixture.Collector.Begin(4);
        var context = fixture.CreateContext();
        context.AddUserMessage("inspect the repository");
        await context.PrepareAsync();

        context.Dispose();
        fixture.Collector.End();
        var snapshot = fixture.Collector.Snapshot();

        snapshot.SummaryCrossCheck.Should().Be("matched");
        snapshot.Complete.Should().BeTrue();
        snapshot.PrepareRecords.Should().ContainSingle().Which.Outcome.Should().Be("Ready");
        snapshot.ModelCallBudget.Should().Be(4);
    }

    /// <summary>Verifies disabled summary logging is represented explicitly.</summary>
    [Fact]
    public async Task Dispose_WithWarningLogging_MarksSummaryUnavailable()
    {
        using var fixture = new SampleTelemetryFixture(warningLogging: true);
        fixture.Collector.Begin(null);
        var context = fixture.CreateContext();
        context.AddUserMessage("inspect");
        await context.PrepareAsync();

        context.Dispose();
        fixture.Collector.End();

        fixture.Collector.Snapshot().SummaryCrossCheck.Should().Be("unavailable");
    }

    /// <summary>Verifies a prepare stopped without an outcome keeps no token estimates.</summary>
    [Fact]
    public async Task CancelledPrepare_WithCancelledSummarizer_MatchesAttemptedSummaryCounts()
    {
        using var fixture = new SampleTelemetryFixture();
        var client = new SampleChatClient(async ct =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return SampleChatClient.Completion();
        });
        fixture.Collector.Begin(3);
        var context = fixture.CreateContext(client);
        context.AddUserMessage(new string('x', 800));
        context.AddUserMessage(new string('y', 800));
        using var cancellation = new CancellationTokenSource();
        var preparation = context.PrepareAsync(cancellation.Token);
        await client.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => preparation);
        context.Dispose();
        fixture.Collector.End();
        var snapshot = fixture.Collector.Snapshot();

        snapshot.PrepareRecords.Should().ContainSingle().Which.Status.Should().Be("incomplete");
        snapshot.PrepareRecords[0].TokensBefore.Should().BeNull();
        snapshot.PrepareRecords[0].TokensAfter.Should().BeNull();
        snapshot.PrepareRecords[0].StrategyRuns.Should().Be(0);
        snapshot.SummarizerCalls.Should().Be(1);
        snapshot.SummarizerFailures.Should().Be(0);
        snapshot.SummaryCrossCheck.Should().Be("matched");
    }

    /// <summary>Verifies the overload used by OpenAISummarizer captures a rejected empty response.</summary>
    [Fact]
    public async Task EmptySummary_ThroughOpenAISummarizer_CapturesReceivedUsageAndFailure()
    {
        using var fixture = new SampleTelemetryFixture();
        var client = new SampleChatClient(_ => Task.FromResult(SampleChatClient.Completion(text: "")));
        fixture.Collector.Begin(3);
        var context = fixture.CreateContext(client);
        context.AddUserMessage(new string('x', 800));
        context.AddUserMessage(new string('y', 800));

        var result = await context.PrepareAsync();
        fixture.Collector.ObservePrepareResult(result.SummarizationError is not null, false);
        context.Dispose();
        fixture.Collector.End();
        var snapshot = fixture.Collector.Snapshot();

        snapshot.SummarizerResponses.Should().ContainSingle().Which.InputTokens.Should().Be(100);
        snapshot.SummarizerResponses[0].OutputTokens.Should().Be(7);
        snapshot.SummarizerFailures.Should().Be(1);
        snapshot.SummarizationErrors.Should().Be(1);
        snapshot.SummaryCrossCheck.Should().Be("matched");
    }

    /// <summary>Verifies mismatched telemetry is detected through the final real summary.</summary>
    [Fact]
    public async Task IncorrectFailureMeasurement_MarksSummaryMismatched()
    {
        using var fixture = new SampleTelemetryFixture();
        fixture.Collector.Begin(null);
        var context = fixture.CreateContext();
        context.AddUserMessage("inspect");
        await context.PrepareAsync();
        fixture.Collector.ObserveMeasurement("tokenguard.summarization.failures", 1, []);

        context.Dispose();
        fixture.Collector.End();

        fixture.Collector.Snapshot().SummaryCrossCheck.Should().Be("mismatched");
    }

    /// <summary>Verifies zero-change strategy activity remains a completed strategy run.</summary>
    [Fact]
    public async Task ReadyPrepare_WithZeroChangeCompaction_RetainsStrategyRun()
    {
        using var fixture = new SampleTelemetryFixture();
        fixture.Collector.Begin(null);
        using var source = new ActivitySource("TokenGuard");
        using (var prepare = source.StartActivity("tokenguard.prepare"))
        {
            using (source.StartActivity("tokenguard.compact"))
            {
            }
            prepare!.SetTag("tokenguard.outcome", "Ready");
            prepare.SetTag("tokenguard.tokens.before", 20);
            prepare.SetTag("tokenguard.tokens.after", 20);
        }
        await Task.CompletedTask;

        fixture.Collector.Snapshot().PrepareRecords.Should().ContainSingle().Which.StrategyRuns.Should().Be(1);
    }

    /// <summary>Verifies only the meter's dropped kind contributes to dropped messages.</summary>
    [Fact]
    public void CompactionMessages_DroppedAndMasked_KeepsSeparateCounts()
    {
        var collector = new SessionMeasurementCollector();
        collector.Begin(null);

        collector.ObserveMeasurement("tokenguard.compaction.messages", 3, [new("tokenguard.kind", "dropped")]);
        collector.ObserveMeasurement("tokenguard.compaction.messages", 10, [new("tokenguard.kind", "masked")]);
        collector.ObserveMeasurement("tokenguard.compaction.messages", 4, [new("tokenguard.kind", "summarized")]);
        collector.ObserveMeasurement("tokenguard.compaction.messages", 99, [new("tokenguard.kind", "unknown")]);
        collector.ObserveMeasurement("tokenguard.health.signals", 1, [new("tokenguard.signal", "RepeatedCompaction")]);

        collector.Snapshot().MessagesDropped.Should().Be(3);
        collector.Snapshot().MessagesMasked.Should().Be(10);
        collector.Snapshot().MessagesSummarized.Should().Be(4);
        var previous = collector.Snapshot();
        collector.End();
        collector.ObserveMeasurement("tokenguard.compaction.messages", 99, [new("tokenguard.kind", "masked")]);
        collector.Snapshot().MessagesMasked.Should().Be(10);
        collector.Begin(null);
        collector.Snapshot().MessagesMasked.Should().Be(0);
        collector.Snapshot().MessagesSummarized.Should().Be(0);
        previous.MessagesMasked.Should().Be(10);
        previous.HealthSignalCounts["RepeatedCompaction"].Should().Be(1);
    }

    /// <summary>Verifies cumulative snapshots retain missing provider usage and independent values.</summary>
    [Fact]
    public void Snapshots_MissingUsageAndLaterUpdates_PreserveEarlierSnapshot()
    {
        var collector = new SessionMeasurementCollector();
        collector.Begin(2);
        collector.ProviderStarted(8);
        collector.ProviderFinished("completed");
        var earlier = collector.Snapshot();

        collector.ProviderStarted(9);
        collector.ProviderFinished("failed");
        collector.End();

        earlier.ProviderCalls.Should().ContainSingle().Which.InputTokens.Should().BeNull();
        collector.Snapshot().ProviderCalls.Should().HaveCount(2);
        collector.Snapshot().ProviderCalls[1].Status.Should().Be("failed");
    }
}
