using FluentAssertions;
using Microsoft.Extensions.Logging;
using TokenGuard.Core;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Diagnostics;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;

namespace TokenGuard.Tests.Diagnostics;

[Collection(TelemetryCollection.Name)]
public sealed class ConversationHealthTests
{
    private const int EstimatorDriftDetected = 6001;
    private const int RepeatedCompactionDetected = 6002;
    private const int LowCompactionYieldDetected = 6003;
    private const int SummarizationFailureStreakDetected = 6004;
    private const int RepeatedOverBudgetDetected = 6005;
    private const int PinnedPressureDetected = 6006;
    private const int HealthSignalCleared = 6050;
    private const int ConversationSummary = 6100;

    [Fact]
    public async Task EstimatorDrift_WhenItStartsHoldsAndStops_LogsOneWarningAndOneClearedRecord()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        using var context = CreateContext(logs, StubCompactionStrategy.Unchanged());
        context.AddUserMessage(Text(20));
        await context.PrepareAsync();

        // Act
        context.RecordModelResponse([new TextContent(Text(5))], providerInputTokens: 40);
        await context.PrepareAsync();
        context.RecordModelResponse([new TextContent(Text(5))], providerInputTokens: 60);
        await context.PrepareAsync();
        context.RecordModelResponse([new TextContent(Text(5))], providerInputTokens: 46);

        // Assert
        var started = logs.WithEventId(EstimatorDriftDetected).Should().ContainSingle().Subject;
        started.Level.Should().Be(LogLevel.Warning);
        started.Property("EstimatedTokens").Should().Be(20);
        started.Property("ProviderInputTokens").Should().Be(40);
        started.Property("DriftPercent").Should().Be(50.0);
        Cleared(logs, "EstimatorDrift").Should().ContainSingle().Which.Level.Should().Be(LogLevel.Information);
    }

    [Fact]
    public async Task RepeatedCompaction_WhenItStartsHoldsAndStops_LogsOneWarningAndOneClearedRecord()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        using var context = CreateContext(logs, StubCompactionStrategy.Unchanged());

        // Act
        context.AddUserMessage(Text(50));
        await context.PrepareAsync();
        context.RecordModelResponse([new TextContent(Text(10))], providerInputTokens: 53);
        await context.PrepareAsync();
        context.AddUserMessage(Text(10));
        await context.PrepareAsync();
        context.RecordModelResponse([new TextContent(Text(10))]);
        await context.PrepareAsync();
        context.AddUserMessage(Text(10));
        await context.PrepareAsync();
        context.RecordModelResponse([new TextContent(Text(5))], providerInputTokens: 5);
        await context.PrepareAsync();

        // Assert
        var started = logs.WithEventId(RepeatedCompactionDetected).Should().ContainSingle().Subject;
        started.Level.Should().Be(LogLevel.Warning);
        started.Property("ConsecutiveTurns").Should().Be(3);
        started.Property("TokensReclaimedPerTurn").Should().Be("0, 3, 0");
        Cleared(logs, "RepeatedCompaction").Should().ContainSingle();
    }

    [Fact]
    public async Task RepeatedCompaction_WhenPrepareIsCalledTwiceOnOneTurn_CountsThatTurnOnce()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        using var context = CreateContext(logs, StubCompactionStrategy.Unchanged());
        context.AddUserMessage(Text(50));

        // Act
        await context.PrepareAsync();
        await context.PrepareAsync();
        await context.PrepareAsync();

        // Assert
        logs.WithEventId(RepeatedCompactionDetected).Should().BeEmpty();
    }

    [Fact]
    public async Task LowCompactionYield_WhenItStartsHoldsAndStops_LogsOneWarningAndOneClearedRecord()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        var strategy = new CountingStrategy(call => call <= 3 ? StubCompactionStrategy.Unchanged() : StubCompactionStrategy.KeepNewest(1));
        using var context = CreateContext(logs, strategy);

        // Act
        context.AddUserMessage(Text(60));
        await context.PrepareAsync();
        context.RecordModelResponse([new TextContent(Text(10))]);
        await context.PrepareAsync();
        context.AddUserMessage(Text(10));
        await context.PrepareAsync();
        context.RecordModelResponse([new TextContent(Text(10))]);
        await context.PrepareAsync();

        // Assert
        var started = logs.WithEventId(LowCompactionYieldDetected).Should().ContainSingle().Subject;
        started.Level.Should().Be(LogLevel.Warning);
        started.Property("TokensBefore").Should().Be(60);
        started.Property("TokensAfter").Should().Be(60);
        started.Property("YieldPercent").Should().Be(0.0);
        Cleared(logs, "LowCompactionYield").Should().ContainSingle();
    }

    [Fact]
    public async Task SummarizationFailureStreak_WhenItStartsHoldsAndStops_LogsOneErrorAndOneClearedRecord()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        var strategy = new CountingStrategy(
            call => call <= 5 ? StubCompactionStrategy.Unchanged(new TimeoutException("provider timed out")) : StubCompactionStrategy.Unchanged());
        using var context = CreateContext(logs, strategy);
        context.AddUserMessage(Text(60));

        // Act
        for (var call = 1; call <= 6; call++)
        {
            await context.PrepareAsync();
        }

        // Assert
        var started = logs.WithEventId(SummarizationFailureStreakDetected).Should().ContainSingle().Subject;
        started.Level.Should().Be(LogLevel.Error);
        started.Property("ConsecutiveFailures").Should().Be(3);
        started.Property("ExceptionType").Should().Be(nameof(TimeoutException));
        Cleared(logs, "SummarizationFailureStreak").Should().ContainSingle();
    }

    [Fact]
    public async Task RepeatedOverBudget_WhenItStartsHoldsAndStops_LogsOneErrorAndOneClearedRecord()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        var strategy = new CountingStrategy(call => call <= 3 ? StubCompactionStrategy.Unchanged() : StubCompactionStrategy.KeepNewest(0));
        using var context = CreateContext(logs, strategy);
        context.AddUserMessage(Text(150));

        // Act
        for (var call = 1; call <= 4; call++)
        {
            await context.PrepareAsync();
        }

        // Assert
        var started = logs.WithEventId(RepeatedOverBudgetDetected).Should().ContainSingle().Subject;
        started.Level.Should().Be(LogLevel.Error);
        started.Property("ConsecutiveCalls").Should().Be(2);
        started.Property("FinalTokens").Should().Be(150);
        started.Property("EffectiveMaxTokens").Should().Be(105L);
        Cleared(logs, "RepeatedOverBudget").Should().ContainSingle();
    }

    [Fact]
    public async Task PinnedPressure_WhenItStartsHoldsAndStops_LogsOneWarningAndOneClearedRecord()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        using var context = CreateContext(logs, StubCompactionStrategy.Unchanged());
        context.SetSystemPrompt(Text(60));

        // Act
        await context.PrepareAsync();
        context.AddUserMessage(Text(5));
        await context.PrepareAsync();
        context.SetSystemPrompt(Text(10));
        await context.PrepareAsync();

        // Assert
        var started = logs.WithEventId(PinnedPressureDetected).Should().ContainSingle().Subject;
        started.Level.Should().Be(LogLevel.Warning);
        started.Property("PinnedTokens").Should().Be(60);
        started.Property("MaxTokens").Should().Be(100);
        started.Property("PinnedPercent").Should().Be(60.0);
        Cleared(logs, "PinnedPressure").Should().ContainSingle();
    }

    [Fact]
    public async Task PinnedPressure_WhenPinnedTokensAreExactlyHalfTheMaximum_LogsNothing()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        using var context = CreateContext(logs, StubCompactionStrategy.Unchanged());
        context.SetSystemPrompt(Text(50));

        // Act
        await context.PrepareAsync();

        // Assert
        logs.WithEventId(PinnedPressureDetected).Should().BeEmpty();
    }

    [Fact]
    public async Task SignalStart_IncrementsTheHealthSignalCounterOnceTaggedWithTheSignalName()
    {
        // Arrange
        using var telemetry = new TelemetryCapture();
        var contextName = $"health-{Guid.NewGuid():N}";
        using var context = CreateContext(new CapturingLoggerFactory(), StubCompactionStrategy.Unchanged(), contextName);
        context.SetSystemPrompt(Text(60));

        // Act
        await context.PrepareAsync();
        await context.PrepareAsync();
        await context.PrepareAsync();

        // Assert
        var measurements = telemetry.MeasurementsOf("tokenguard.health.signals", contextName);
        measurements.Should().ContainSingle(m => "PinnedPressure".Equals(m.Tag("tokenguard.signal"))).Which.Value.Should().Be(1);
    }

    [Fact]
    public async Task Dispose_AfterAConversation_LogsOneSummaryRecordWithItsTotals()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();

        // Act
        await TokenGuardTelemetryTests.RunScriptedConversationAsync("summary", logs);

        // Assert
        var summary = logs.WithEventId(ConversationSummary).Should().ContainSingle().Subject;
        summary.Level.Should().Be(LogLevel.Information);
        summary.Property("Turns").Should().Be(4);
        summary.Property("PrepareCalls").Should().Be(4);
        summary.Property("StrategyRuns").Should().Be(3);
        summary.Property("TokensReclaimed").Should().Be(149L);
        summary.Property("SummarizerCalls").Should().Be(1);
        summary.Property("SummarizerFailures").Should().Be(1);
        summary.Property("EmergencyTruncations").Should().Be(2);
        summary.Property("PeakPreparedTokens").Should().Be(120);
        summary.Property("LargestDriftPercent").Should().BeOfType<double>().Which.Should().BeApproximately(20.0, 1e-9);
        logs.Records.Last().Should().BeSameAs(summary);
    }

    [Fact]
    public void Dispose_WhenPrepareWasNeverCalled_LogsNoSummary()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        var context = CreateContext(logs, StubCompactionStrategy.Unchanged());
        context.AddUserMessage(Text(20));

        // Act
        context.Dispose();

        // Assert
        logs.WithEventId(ConversationSummary).Should().BeEmpty();
    }

    [Fact]
    public async Task Dispose_WhenCalledTwice_LogsTheSummaryOnce()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        var context = CreateContext(logs, StubCompactionStrategy.Unchanged());
        context.AddUserMessage(Text(20));
        await context.PrepareAsync();

        // Act
        context.Dispose();
        context.Dispose();

        // Assert
        logs.WithEventId(ConversationSummary).Should().ContainSingle();
    }

    private static IReadOnlyList<CapturedLogRecord> Cleared(CapturingLoggerFactory logs, string signal) =>
        logs.WithEventId(HealthSignalCleared).Where(record => signal.Equals(record.Property("Signal"))).ToArray();

    private static ConversationContext CreateContext(
        CapturingLoggerFactory logs, ICompactionStrategy strategy, string contextName = ConversationDiagnostics.DefaultContextName)
    {
        var budget = new ContextBudget(100, compactionThreshold: 0.5, emergencyThreshold: null, overrunTolerance: 0.05);
        return new ConversationContext(budget, new TextLengthTokenCounter(), strategy, new ConversationDiagnostics(logs, contextName));
    }

    private static string Text(int length) => new('a', length);

    private sealed class CountingStrategy(Func<int, ICompactionStrategy> strategyForCall) : ICompactionStrategy
    {
        private int _calls;

        public Task<CompactionResult> CompactAsync(
            IReadOnlyList<ContextMessage> messages, int availableTokens, CancellationToken cancellationToken = default) =>
            strategyForCall(++this._calls).CompactAsync(messages, availableTokens, cancellationToken);
    }
}
