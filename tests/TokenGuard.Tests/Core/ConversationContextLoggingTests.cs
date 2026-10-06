using FluentAssertions;
using Microsoft.Extensions.Logging;
using TokenGuard.Core;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Diagnostics;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Exceptions;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;
using TokenGuard.Tests.Diagnostics;

namespace TokenGuard.Tests.Core;

public sealed class ConversationContextLoggingTests
{
    private const int MessageRecorded = 1000;
    private const int EstimateAnchored = 1001;
    private const int PrepareBelowTrigger = 1010;
    private const int CompactionCompleted = 1011;
    private const int EmergencyTruncationApplied = 1012;
    private const int SummarizationFailed = 1013;
    private const int PrepareOverBudget = 1014;
    private const int PinnedBudgetExceeded = 1015;
    private const int EmergencyTruncationEvaluated = 1016;

    public static TheoryData<string, MessageRole, bool, int> RecordingMethods => new()
    {
        { nameof(IConversationContext.SetSystemPrompt), MessageRole.System, true, 1 },
        { "AddPinnedMessageText", MessageRole.User, true, 1 },
        { "AddPinnedMessageSegments", MessageRole.Model, true, 2 },
        { nameof(IConversationContext.AddUserMessage), MessageRole.User, false, 1 },
        { nameof(IConversationContext.RecordModelResponse), MessageRole.Model, false, 2 },
        { nameof(IConversationContext.RecordToolResult), MessageRole.Tool, false, 1 },
    };

    [Fact]
    public async Task PrepareAsync_WhenBelowTrigger_LogsOneDebugRecordWithTokenTotals()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        using var context = CreateContext(logs, StubCompactionStrategy.Unchanged());
        context.AddUserMessage(Text(20));

        // Act
        await context.PrepareAsync();

        // Assert
        var record = logs.WithEventId(PrepareBelowTrigger).Should().ContainSingle().Subject;
        record.Level.Should().Be(LogLevel.Debug);
        record.Property("TotalTokens").Should().Be(20);
        record.Property("TriggerTokens").Should().Be(50);
        record.Property("MaxTokens").Should().Be(100);
        logs.WithEventId(CompactionCompleted).Should().BeEmpty();
    }

    [Fact]
    public async Task PrepareAsync_WhenStrategyRan_LogsOneInformationRecordWithCompactionResult()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        using var context = CreateContext(logs, StubCompactionStrategy.KeepNewest(1));
        context.AddUserMessage(Text(40));
        context.RecordModelResponse([new TextContent(Text(30))]);

        // Act
        await context.PrepareAsync();

        // Assert
        var record = logs.WithEventId(CompactionCompleted).Should().ContainSingle().Subject;
        record.Level.Should().Be(LogLevel.Information);
        record.Property("Outcome").Should().Be(PrepareOutcome.Compacted);
        record.Property("TokensBefore").Should().Be(70);
        record.Property("TokensAfter").Should().Be(30);
        record.Property("MessagesCompacted").Should().Be(1);
        record.Property("EmergencyMessagesDropped").Should().Be(0);
        record.Property("StrategyName").Should().Be(StubCompactionStrategy.Name);
        record.Property("ElapsedMilliseconds").Should().BeOfType<double>().Which.Should().BeGreaterThanOrEqualTo(0);
        logs.WithEventId(PrepareBelowTrigger).Should().BeEmpty();
    }

    [Fact]
    public async Task PrepareAsync_WhenEmergencyTruncationDropsMessages_LogsOneWarningWithCountAndTokens()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        using var context = CreateContext(logs, StubCompactionStrategy.Unchanged());
        context.AddUserMessage(Text(60));
        await context.PrepareAsync();
        context.RecordModelResponse([new TextContent(Text(60))]);
        context.AddUserMessage(Text(60));

        // Act
        var result = await context.PrepareAsync();

        // Assert
        result.MessagesDropped.Should().Be(2);
        var record = logs.WithEventId(EmergencyTruncationApplied).Should().ContainSingle().Subject;
        record.Level.Should().Be(LogLevel.Warning);
        record.Property("MessagesDropped").Should().Be(2);
        record.Property("TokensBefore").Should().Be(180);
        record.Property("TokensAfter").Should().Be(60);
        logs.WithEventId(CompactionCompleted).Last().Property("EmergencyMessagesDropped").Should().Be(2);
    }

    [Fact]
    public async Task PrepareAsync_WhenEmergencyTruncationDropsMessages_LogsOneDebugRecordWithTheEvaluation()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        using var context = CreateContext(logs, StubCompactionStrategy.Unchanged());
        context.AddUserMessage(Text(60));
        await context.PrepareAsync();
        context.RecordModelResponse([new TextContent(Text(60))]);
        context.AddUserMessage(Text(60));

        // Act
        await context.PrepareAsync();

        // Assert
        var record = logs.WithEventId(EmergencyTruncationEvaluated).Should().ContainSingle().Subject;
        record.Level.Should().Be(LogLevel.Debug);
        record.Property("CurrentTokens").Should().Be(180);
        record.Property("EmergencyTriggerTokens").Should().Be(100);
        record.Property("TurnGroups").Should().Be(2);
        record.Property("TurnGroupsDropped").Should().Be(2);
        record.Property("PreservedFloorIndex").Should().Be(2);
        record.Property("FloorExceedsTrigger").Should().Be(false);
    }

    [Fact]
    public async Task PrepareAsync_WhenThePreservedFloorAloneExceedsTheTrigger_LogsThatNothingCouldBeDropped()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        using var context = CreateContext(logs, StubCompactionStrategy.Unchanged());
        context.AddUserMessage(Text(150));

        // Act
        await context.PrepareAsync();

        // Assert
        var record = logs.WithEventId(EmergencyTruncationEvaluated).Should().ContainSingle().Subject;
        record.Property("TurnGroups").Should().Be(0);
        record.Property("TurnGroupsDropped").Should().Be(0);
        record.Property("PreservedFloorIndex").Should().Be(0);
        record.Property("FloorExceedsTrigger").Should().Be(true);
        logs.WithEventId(EmergencyTruncationApplied).Should().BeEmpty();
    }

    [Fact]
    public async Task PrepareAsync_WhenPreparedPayloadIsWithinTheEmergencyTrigger_LogsNoEmergencyEvaluation()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        using var context = CreateContext(logs, StubCompactionStrategy.Unchanged());
        context.AddUserMessage(Text(60));

        // Act
        await context.PrepareAsync();

        // Assert
        logs.WithEventId(EmergencyTruncationEvaluated).Should().BeEmpty();
    }

    [Fact]
    public async Task PrepareAsync_WhenTheStrategyLogs_ItsRecordsCarryTheConversationScope()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        var strategy = new ScopeProbeStrategy(logs.CreateLogger("Probe"));
        using var context = CreateContext(logs, strategy, contextName: "research");
        context.AddUserMessage(Text(60));

        // Act
        await context.PrepareAsync();

        // Assert
        var contextRecord = logs.WithEventId(CompactionCompleted).Single();
        var strategyRecord = logs.Records.Single(record => record.Category == "Probe");
        strategyRecord.Property("ConversationId").Should().Be(contextRecord.Property("ConversationId"));
        strategyRecord.Property("ContextName").Should().Be("research");
        strategyRecord.Property("Turn").Should().Be(1);
    }

    [Fact]
    public async Task PrepareAsync_WhenTheStrategyReturned_TheConversationScopeIsClosed()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        using var context = CreateContext(logs, StubCompactionStrategy.Unchanged());
        context.AddUserMessage(Text(60));

        // Act
        await context.PrepareAsync();

        // Assert
        logs.Records.Should().OnlyContain(record => record.Scope.Count == 0);
    }

    [Fact]
    public async Task PrepareAsync_WhenSummarizationErrorIsSet_LogsOneWarningWithTheException()
    {
        // Arrange
        var failure = new TimeoutException("provider timed out");
        var logs = new CapturingLoggerFactory();
        using var context = CreateContext(logs, StubCompactionStrategy.Unchanged(failure));
        context.AddUserMessage(Text(60));

        // Act
        await context.PrepareAsync();

        // Assert
        var record = logs.WithEventId(SummarizationFailed).Should().ContainSingle().Subject;
        record.Level.Should().Be(LogLevel.Warning);
        record.Exception.Should().BeSameAs(failure);
        record.Property("StrategyName").Should().Be(StubCompactionStrategy.Name);
    }

    [Theory]
    [InlineData(0, PrepareOutcome.CannotCompact)]
    [InlineData(1, PrepareOutcome.CompactionInsufficient)]
    public async Task PrepareAsync_WhenPreparedPayloadIsOverBudget_LogsOneErrorWithOutcome(int messagesAffected, PrepareOutcome expectedOutcome)
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        var strategy = new StubCompactionStrategy((messages, _) => new CompactionResult(messages, 150, 150, messagesAffected, StubCompactionStrategy.Name));
        using var context = CreateContext(logs, strategy, emergencyThreshold: null);
        context.AddUserMessage(Text(150));

        // Act
        var result = await context.PrepareAsync();

        // Assert
        result.Outcome.Should().Be(expectedOutcome);
        var record = logs.WithEventId(PrepareOverBudget).Should().ContainSingle().Subject;
        record.Level.Should().Be(LogLevel.Error);
        record.Property("Outcome").Should().Be(expectedOutcome);
        record.Property("FinalTokens").Should().Be(150);
        record.Property("EffectiveMaxTokens").Should().Be(105L);
    }

    [Fact]
    public async Task PrepareAsync_WhenPinnedMessagesExceedMaxTokens_LogsOneErrorBeforeThrowing()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        using var context = CreateContext(logs, StubCompactionStrategy.Unchanged());
        context.SetSystemPrompt(Text(120));

        // Act
        var act = () => context.PrepareAsync();

        // Assert
        await act.Should().ThrowAsync<PinnedTokenBudgetExceededException>();
        var record = logs.WithEventId(PinnedBudgetExceeded).Should().ContainSingle().Subject;
        record.Level.Should().Be(LogLevel.Error);
        record.Property("PinnedTokens").Should().Be(120);
        record.Property("MaxTokens").Should().Be(100);
    }

    [Theory]
    [MemberData(nameof(RecordingMethods))]
    public void RecordingMethod_LogsOneTraceRecordWithRolePinnedFlagAndSegmentCount(string method, MessageRole role, bool isPinned, int segmentCount)
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        using var context = CreateContext(logs, StubCompactionStrategy.Unchanged());

        // Act
        Record(context, method);

        // Assert
        var record = logs.WithEventId(MessageRecorded).Should().ContainSingle().Subject;
        record.Level.Should().Be(LogLevel.Trace);
        record.Property("Role").Should().Be(role);
        record.Property("IsPinned").Should().Be(isPinned);
        record.Property("SegmentCount").Should().Be(segmentCount);
    }

    [Fact]
    public void SetSystemPrompt_WhenReplacingTheExistingPrompt_LogsOneTraceRecordForTheReplacement()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        using var context = CreateContext(logs, StubCompactionStrategy.Unchanged());
        context.SetSystemPrompt(Text(10));

        // Act
        context.SetSystemPrompt(Text(12));

        // Assert
        logs.WithEventId(MessageRecorded).Should().HaveCount(2);
    }

    [Fact]
    public async Task RecordModelResponse_WithProviderInputTokens_LogsOneDebugRecordWithTheCorrection()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        using var context = CreateContext(logs, StubCompactionStrategy.Unchanged());
        context.AddUserMessage(Text(20));
        await context.PrepareAsync();

        // Act
        context.RecordModelResponse([new TextContent(Text(5))], providerInputTokens: 26);

        // Assert
        var record = logs.WithEventId(EstimateAnchored).Should().ContainSingle().Subject;
        record.Level.Should().Be(LogLevel.Debug);
        record.Property("ProviderInputTokens").Should().Be(26);
        record.Property("LastEstimatedTokens").Should().Be(20);
        record.Property("Correction").Should().Be(6);
    }

    [Fact]
    public void RecordModelResponse_WithoutProviderInputTokens_LogsNoCorrection()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        using var context = CreateContext(logs, StubCompactionStrategy.Unchanged());

        // Act
        context.RecordModelResponse([new TextContent(Text(5))]);

        // Assert
        logs.WithEventId(EstimateAnchored).Should().BeEmpty();
    }

    [Fact]
    public async Task EveryRecord_CarriesTheConversationIdAndContextName()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        using var context = CreateContext(logs, StubCompactionStrategy.KeepNewest(1), contextName: "research");
        context.SetSystemPrompt(Text(5));
        context.AddUserMessage(Text(20));
        await context.PrepareAsync();
        context.RecordModelResponse([new TextContent(Text(40))], providerInputTokens: 30);

        // Act
        await context.PrepareAsync();

        // Assert
        var records = logs.Records;
        records.Should().NotBeEmpty();
        records.Select(record => record.Property("ConversationId")).Distinct().Should().ContainSingle().Which.Should().BeOfType<string>()
            .Which.Should().NotBeEmpty();
        records.Should().OnlyContain(record => "research".Equals(record.Property("ContextName")));
    }

    [Fact]
    public async Task PrepareRecords_CarryTheTurnNumber()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        using var context = CreateContext(logs, StubCompactionStrategy.KeepNewest(1));
        context.AddUserMessage(Text(20));
        await context.PrepareAsync();
        context.RecordModelResponse([new TextContent(Text(40))]);

        // Act
        await context.PrepareAsync();

        // Assert
        logs.WithEventId(PrepareBelowTrigger).Should().ContainSingle().Which.Property("Turn").Should().Be(1);
        logs.WithEventId(CompactionCompleted).Should().ContainSingle().Which.Property("Turn").Should().Be(2);
    }

    [Fact]
    public void TwoContexts_ReportDifferentConversationIds()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        using var first = CreateContext(logs, StubCompactionStrategy.Unchanged());
        using var second = CreateContext(logs, StubCompactionStrategy.Unchanged());

        // Act
        first.AddUserMessage(Text(5));
        second.AddUserMessage(Text(5));

        // Assert
        logs.Records.Select(record => record.Property("ConversationId")).Distinct().Should().HaveCount(2);
    }

    [Fact]
    public void Records_UseTheConversationContextCategory()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        using var context = CreateContext(logs, StubCompactionStrategy.Unchanged());

        // Act
        context.AddUserMessage(Text(5));

        // Assert
        logs.Records.Should().ContainSingle().Which.Category.Should().Be("TokenGuard.Core.ConversationContext");
    }

    private static ConversationContext CreateContext(
        CapturingLoggerFactory logs,
        ICompactionStrategy strategy,
        double? emergencyThreshold = 1.0,
        string contextName = ConversationDiagnostics.DefaultContextName)
    {
        var budget = new ContextBudget(100, compactionThreshold: 0.5, emergencyThreshold: emergencyThreshold, overrunTolerance: 0.05);
        return new ConversationContext(budget, new TextLengthTokenCounter(), strategy, new ConversationDiagnostics(logs, contextName));
    }

    private static void Record(ConversationContext context, string method)
    {
        switch (method)
        {
            case nameof(IConversationContext.SetSystemPrompt):
                context.SetSystemPrompt(Text(5));
                break;
            case "AddPinnedMessageText":
                context.AddPinnedMessage(MessageRole.User, Text(5));
                break;
            case "AddPinnedMessageSegments":
                context.AddPinnedMessage(MessageRole.Model, [new TextContent(Text(5)), new TextContent(Text(5))]);
                break;
            case nameof(IConversationContext.AddUserMessage):
                context.AddUserMessage(Text(5));
                break;
            case nameof(IConversationContext.RecordModelResponse):
                context.RecordModelResponse([new TextContent(Text(5)), new ToolUseContent("call_1", "search", "{}")]);
                break;
            case nameof(IConversationContext.RecordToolResult):
                context.RecordToolResult("call_1", "search", Text(5));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(method), method, "Unknown recording method.");
        }
    }

    private static string Text(int length) => new('a', length);

    private sealed class ScopeProbeStrategy(ILogger logger) : ICompactionStrategy
    {
        public Task<CompactionResult> CompactAsync(
            IReadOnlyList<ContextMessage> messages, int availableTokens, CancellationToken cancellationToken = default)
        {
            logger.LogDebug("strategy ran");
            return Task.FromResult(new CompactionResult(messages, 0, 0, 0, nameof(ScopeProbeStrategy)));
        }
    }
}
