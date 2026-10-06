using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TokenGuard.Core;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Diagnostics;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Exceptions;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;
using TokenGuard.Core.Options;
using TokenGuard.Core.Strategies;

namespace TokenGuard.Tests.Diagnostics;

[Collection(TelemetryCollection.Name)]
public sealed class TokenGuardTelemetryTests
{
    private const string Prepare = "tokenguard.prepare";
    private const string Compact = "tokenguard.compact";
    private const string Summarize = "tokenguard.summarize";

    [Fact]
    public void Names_AreTokenGuard()
    {
        // Arrange
        using var capture = new TelemetryCapture();

        // Act
        var source = TokenGuardTelemetry.ActivitySource;
        var meter = TokenGuardTelemetry.Meter;

        // Assert
        source.Name.Should().Be(TokenGuardDiagnostics.ActivitySourceName).And.Be("TokenGuard");
        meter.Name.Should().Be(TokenGuardDiagnostics.MeterName).And.Be("TokenGuard");
        source.Version.Should().NotBeNullOrEmpty().And.Be(meter.Version);
    }

    [Fact]
    public async Task ScriptedConversation_EmitsOnePrepareActivityPerCallWithItsTags()
    {
        // Arrange
        using var capture = new TelemetryCapture();
        var contextName = NewContextName();

        // Act
        var outcomes = await RunScriptedConversationAsync(contextName);

        // Assert
        outcomes.Should().Equal(PrepareOutcome.Ready, PrepareOutcome.Compacted, PrepareOutcome.Compacted, PrepareOutcome.CompactionInsufficient);
        var prepares = capture.ActivitiesNamed(Prepare);
        prepares.Should().HaveCount(4);
        prepares.Select(activity => activity.GetTagItem("tokenguard.outcome")).Should().Equal(outcomes.Select(outcome => (object)outcome.ToString()));
        prepares.Select(activity => activity.GetTagItem("tokenguard.turn")).Should().Equal(1, 2, 3, 4);
        prepares.Select(activity => activity.GetTagItem("tokenguard.tokens.before")).Should().Equal(20, 85, 110, 240);
        prepares.Select(activity => activity.GetTagItem("tokenguard.tokens.after")).Should().Equal(20, 78, 88, 120);
        prepares.Select(activity => activity.GetTagItem("tokenguard.messages.compacted")).Should().Equal(0, 1, 2, 7);
        prepares.Should().OnlyContain(activity => contextName.Equals(activity.GetTagItem("tokenguard.context.name")));
        prepares.Should().OnlyContain(activity => 100.Equals(activity.GetTagItem("tokenguard.tokens.max")));
        prepares.Select(activity => activity.GetTagItem("tokenguard.conversation.id")).Distinct().Should().ContainSingle()
            .Which.Should().BeOfType<string>().Which.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ScriptedConversation_SetsPrepareStatusFromTheOutcome()
    {
        // Arrange
        using var capture = new TelemetryCapture();

        // Act
        await RunScriptedConversationAsync(NewContextName());

        // Assert
        capture.ActivitiesNamed(Prepare).Select(activity => activity.Status)
            .Should().Equal(ActivityStatusCode.Ok, ActivityStatusCode.Ok, ActivityStatusCode.Ok, ActivityStatusCode.Error);
    }

    [Fact]
    public async Task ScriptedConversation_WrapsEachStrategyCallInACompactActivityUnderPrepare()
    {
        // Arrange
        using var capture = new TelemetryCapture();

        // Act
        await RunScriptedConversationAsync(NewContextName());

        // Assert
        var prepares = capture.ActivitiesNamed(Prepare);
        var compacts = capture.ActivitiesNamed(Compact);
        compacts.Should().HaveCount(3);
        compacts.Select(activity => activity.Parent).Should().Equal(prepares.Skip(1));
        compacts.Should().OnlyContain(activity => nameof(TieredCompactionStrategy).Equals(activity.GetTagItem("tokenguard.strategy")));
        compacts.Should().OnlyContain(activity => 100.Equals(activity.GetTagItem("tokenguard.tokens.available")));
        compacts.Select(activity => activity.GetTagItem("tokenguard.tokens.before")).Should().Equal(80, 110, 240);
        compacts.Select(activity => activity.GetTagItem("tokenguard.tokens.after")).Should().Equal(78, 108, 238);
        compacts.Should().OnlyContain(activity => 1.Equals(activity.GetTagItem("tokenguard.messages.affected")));
    }

    [Fact]
    public async Task ScriptedConversation_RecordsTheFailedSummarizerCallAsAnErrorActivityUnderCompact()
    {
        // Arrange
        using var capture = new TelemetryCapture();

        // Act
        await RunScriptedConversationAsync(NewContextName());

        // Assert
        var summarize = capture.ActivitiesNamed(Summarize).Should().ContainSingle().Subject;
        summarize.Parent.Should().BeSameAs(capture.ActivitiesNamed(Compact)[1]);
        summarize.GetTagItem("tokenguard.messages.count").Should().Be(1);
        summarize.GetTagItem("tokenguard.tokens.target").Should().Be(10);
        summarize.Status.Should().Be(ActivityStatusCode.Error);
        summarize.Events.Should().ContainSingle().Which.Name.Should().Be("exception");
        capture.ActivitiesNamed(Prepare)[2].Status.Should().Be(ActivityStatusCode.Ok);
    }

    [Fact]
    public async Task ScriptedConversation_RecordsEmergencyTruncationAsAnEventOnPrepare()
    {
        // Arrange
        using var capture = new TelemetryCapture();

        // Act
        await RunScriptedConversationAsync(NewContextName());

        // Assert
        var prepares = capture.ActivitiesNamed(Prepare);
        prepares[0].Events.Should().BeEmpty();
        prepares[1].Events.Should().BeEmpty();
        var truncation = prepares[2].Events.Should().ContainSingle().Subject;
        truncation.Name.Should().Be("tokenguard.emergency_truncation");
        truncation.Tags.Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, object?>("tokenguard.messages.dropped", 1));
        prepares[3].Events.Should().ContainSingle().Which.Tags.Single().Value.Should().Be(6);
    }

    [Fact]
    public async Task ScriptedConversation_RecordsEveryInstrument()
    {
        // Arrange
        using var capture = new TelemetryCapture();
        var contextName = NewContextName();

        // Act
        await RunScriptedConversationAsync(contextName);

        // Assert
        var prepareCounts = capture.MeasurementsOf("tokenguard.prepare.count", contextName);
        prepareCounts.Should().HaveCount(4).And.OnlyContain(m => m.Value == 1);
        prepareCounts.Select(m => m.Tag("tokenguard.outcome")).Should().Equal("Ready", "Compacted", "Compacted", "CompactionInsufficient");

        var prepareDurations = capture.MeasurementsOf("tokenguard.prepare.duration", contextName);
        prepareDurations.Should().HaveCount(4).And.OnlyContain(m => m.Unit == "s" && m.Value >= 0);
        prepareDurations.Select(m => m.Tag("tokenguard.outcome")).Should().Equal("Ready", "Compacted", "Compacted", "CompactionInsufficient");

        var compactionDurations = capture.MeasurementsOf("tokenguard.compaction.duration", contextName);
        compactionDurations.Should().HaveCount(3).And.OnlyContain(m => m.Unit == "s" && m.Value >= 0);
        compactionDurations.Should().OnlyContain(m => nameof(TieredCompactionStrategy).Equals(m.Tag("tokenguard.strategy")));

        var summarizationDurations = capture.MeasurementsOf("tokenguard.summarization.duration", contextName);
        summarizationDurations.Should().ContainSingle().Which.Tag("tokenguard.result").Should().Be("failure");
        capture.MeasurementsOf("tokenguard.summarization.failures", contextName).Should().ContainSingle().Which.Value.Should().Be(1);

        capture.MeasurementsOf("tokenguard.token_counting.duration", contextName).Should().HaveCount(4).And.OnlyContain(m => m.Unit == "s");

        var contextTokens = capture.MeasurementsOf("tokenguard.context.tokens", contextName);
        contextTokens.Select(m => m.Value).Should().Equal(20, 78, 88, 120);
        contextTokens.Select(m => m.Tag("tokenguard.outcome")).Should().Equal("Ready", "Compacted", "Compacted", "CompactionInsufficient");

        capture.MeasurementsOf("tokenguard.compaction.tokens_reclaimed", contextName).Select(m => m.Value).Should().Equal(7, 22, 120);

        var messages = capture.MeasurementsOf("tokenguard.compaction.messages", contextName);
        messages.Where(m => "masked".Equals(m.Tag("tokenguard.kind"))).Select(m => m.Value).Should().Equal(1, 1, 1);
        messages.Where(m => "dropped".Equals(m.Tag("tokenguard.kind"))).Select(m => m.Value).Should().Equal(1, 6);

        capture.MeasurementsOf("tokenguard.emergency_truncation.count", contextName).Should().HaveCount(2).And.OnlyContain(m => m.Value == 1);

        var errorRatio = capture.MeasurementsOf("tokenguard.estimate.error_ratio", contextName).Should().ContainSingle().Subject;
        errorRatio.Value.Should().BeApproximately(0.2, 1e-9);
        errorRatio.Unit.Should().Be("1");
    }

    [Fact]
    public async Task ScriptedConversation_TagsMetricsOnlyWithTheAllowedNames()
    {
        // Arrange
        using var capture = new TelemetryCapture();
        var contextName = NewContextName();
        string[] allowed =
        [
            "tokenguard.context.name", "tokenguard.strategy", "tokenguard.outcome", "tokenguard.kind", "tokenguard.result", "tokenguard.signal",
        ];

        // Act
        await RunScriptedConversationAsync(contextName);

        // Assert
        capture.Measurements.Should().NotBeEmpty();
        capture.Measurements.SelectMany(m => m.Tags.Keys).Distinct().Should().BeSubsetOf(allowed);
    }

    [Fact]
    public async Task SummarizerCall_WhenItSucceeds_RecordsASuccessDurationAndCountsSummarizedMessages()
    {
        // Arrange
        using var capture = new TelemetryCapture();
        var contextName = NewContextName();
        using var context = CreateContext(contextName, ScriptedSummarizer.Returning(new string('s', 10)), emergencyThreshold: null);
        context.AddUserMessage(Text('a', 40));
        await context.PrepareAsync();
        context.RecordModelResponse([new TextContent(Text('b', 40))]);
        context.AddUserMessage(Text('c', 30));

        // Act
        var result = await context.PrepareAsync();

        // Assert
        result.Outcome.Should().Be(PrepareOutcome.Compacted);
        capture.MeasurementsOf("tokenguard.summarization.duration", contextName).Should().ContainSingle()
            .Which.Tag("tokenguard.result").Should().Be("success");
        capture.MeasurementsOf("tokenguard.summarization.failures", contextName).Should().BeEmpty();
        capture.MeasurementsOf("tokenguard.compaction.messages", contextName)
            .Should().ContainSingle(m => "summarized".Equals(m.Tag("tokenguard.kind"))).Which.Value.Should().Be(1);
        capture.ActivitiesNamed(Summarize).Should().ContainSingle().Which.Status.Should().Be(ActivityStatusCode.Unset);
    }

    [Fact]
    public async Task PrepareAsync_WhenPinnedMessagesExceedMaxTokens_SetsErrorStatusOnThePrepareActivity()
    {
        // Arrange
        using var capture = new TelemetryCapture();
        var contextName = NewContextName();
        using var context = CreateContext(contextName, summarizer: null);
        context.SetSystemPrompt(Text('p', 120));

        // Act
        var act = () => context.PrepareAsync();

        // Assert
        await act.Should().ThrowAsync<PinnedTokenBudgetExceededException>();
        capture.ActivitiesNamed(Prepare).Should().ContainSingle().Which.Status.Should().Be(ActivityStatusCode.Error);
        capture.MeasurementsOf("tokenguard.prepare.count", contextName).Should().BeEmpty();
    }

    [Fact]
    public async Task PrepareAsync_WithoutAnyListener_CreatesNoActivityAndLeavesInstrumentsDisabled()
    {
        // Arrange
        Activity? activitySeenByStrategy = new("placeholder");
        var strategy = new StubCompactionStrategy((messages, _) =>
        {
            activitySeenByStrategy = Activity.Current;
            return new CompactionResult(messages, 60, 60, 0, StubCompactionStrategy.Name);
        });
        var counter = new TimedTokenCounter(new TextLengthTokenCounter());
        using var context = new ConversationContext(
            CreateBudget(emergencyThreshold: 1.0), counter, strategy, new ConversationDiagnostics(NullLoggerFactory.Instance, NewContextName()));
        context.AddUserMessage(Text('a', 60));

        // Act
        await context.PrepareAsync();

        // Assert
        activitySeenByStrategy.Should().BeNull();
        TokenGuardTelemetry.ActivitySource.HasListeners().Should().BeFalse();
        TokenGuardTelemetry.PrepareDuration.Enabled.Should().BeFalse();
        TokenGuardTelemetry.TokenCountingDuration.Enabled.Should().BeFalse();
        counter.StopTiming().Should().Be(TimeSpan.Zero);
    }

    /// <summary>
    ///     Drives one conversation through a below-trigger call, a masking call, a failed summarization with emergency
    ///     truncation, and an over-budget call.
    /// </summary>
    /// <param name="contextName">The context name the conversation reports with.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the outcome of each prepare call.</returns>
    internal static async Task<IReadOnlyList<PrepareOutcome>> RunScriptedConversationAsync(string contextName, ILoggerFactory? loggerFactory = null)
    {
        using var context = CreateContext(
            contextName, ScriptedSummarizer.Throwing(new TimeoutException("provider timed out")), loggerFactory: loggerFactory);
        var outcomes = new List<PrepareOutcome>();

        context.AddUserMessage(Text('a', 20));
        outcomes.Add((await context.PrepareAsync()).Outcome);

        context.RecordModelResponse([new ToolUseContent("call_1", "search", Text('q', 10))], providerInputTokens: 25);
        context.RecordToolResult("call_1", "search", Text('r', 40));
        context.RecordModelResponse([new TextContent(Text('b', 10))]);
        outcomes.Add((await context.PrepareAsync()).Outcome);

        context.AddUserMessage(Text('c', 30));
        outcomes.Add((await context.PrepareAsync()).Outcome);

        context.RecordModelResponse([new TextContent(Text('d', 10))]);
        context.AddUserMessage(Text('e', 120));
        outcomes.Add((await context.PrepareAsync()).Outcome);

        return outcomes;
    }

    private static ConversationContext CreateContext(
        string contextName, ILlmSummarizer? summarizer, double? emergencyThreshold = 1.0, ILoggerFactory? loggerFactory = null)
    {
        var diagnostics = new ConversationDiagnostics(loggerFactory ?? NullLoggerFactory.Instance, contextName);
        var counter = new TimedTokenCounter(new TextLengthTokenCounter());
        var loggers = diagnostics.LoggerFactory;
        var summarization = summarizer is null
            ? null
            : new LlmSummarizationStrategy(
                summarizer, counter, new LlmSummarizationOptions(windowSize: 2, minSummaryTokens: 5, maxSummaryTokens: 50),
                loggers.CreateLogger<LlmSummarizationStrategy>(), diagnostics);
        var strategy = new TieredCompactionStrategy(
            counter, new SlidingWindowOptions(windowSize: 1, protectedWindowFraction: 0.20), summarization,
            loggers.CreateLogger<TieredCompactionStrategy>(), loggers.CreateLogger<SlidingWindowStrategy>());

        return new ConversationContext(CreateBudget(emergencyThreshold), counter, strategy, diagnostics);
    }

    private static ContextBudget CreateBudget(double? emergencyThreshold) =>
        new(100, compactionThreshold: 0.5, emergencyThreshold: emergencyThreshold, overrunTolerance: 0.05);

    private static string NewContextName() => $"telemetry-{Guid.NewGuid():N}";

    private static string Text(char fill, int length) => new(fill, length);
}
