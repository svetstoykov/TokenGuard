using FluentAssertions;
using TokenGuard.Core.Contexts;
using TokenGuard.Core.Options;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Strategies;
using TokenGuard.Core.TokenCounting;

namespace TokenGuard.IntegrationTests;

public sealed class ConversationContextIntegrationTests
{
    [Fact]
    public async Task PrepareAsync_WhenLargeToolResultPushesHistoryOverThreshold_MasksOldToolResultAndPreservesRecentMessages()
    {
        // Arrange
        var budget = new ContextBudget(maxTokens: 1000, compactionThreshold: 0.80);
        var counter = new EstimatedTokenCounter();
        var strategy = new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 2, protectedWindowFraction: 0.5));
        var engine = new ConversationContext(budget, counter, strategy);
        
        engine.SetSystemPrompt("You are a helpful assistant.");
        engine.AddUserMessage("Please analyze the logs for the last 24 hours.");
        
        var toolUse = new ToolUseContent("call_123", "analyze_logs", "{\"timespan\":\"24h\"}");
        engine.RecordModelResponse([toolUse]);
        
        var massiveLog = new string('A', 4000);
        engine.RecordToolResult("call_123", "analyze_logs", massiveLog);
        engine.RecordModelResponse([new TextContent("The logs show that the system was running normally, but there was a spike in memory usage at 3 AM.")]);
        engine.AddUserMessage("Can you check the database logs around 3 AM?");
        
        var secondToLast = engine.History[^2];
        var last = engine.History[^1];

        // Act
        var compactedMessages = await engine.PrepareAsync();

        // Assert
        compactedMessages.Should().NotBeSameAs(engine.History,
            because: "preparing an over-budget conversation should return a compacted view");

        var compactedToolResult = compactedMessages.FirstOrDefault(m =>
            m.Role == MessageRole.Tool &&
            m.Content.Any(c => c is TextContent tc && tc.Content.Contains("[Tool result cleared", StringComparison.OrdinalIgnoreCase)));

        compactedToolResult.Should().NotBeNull(
            because: "old oversized tool output should be masked during compaction");
        compactedToolResult!.State.Should().Be(CompactionState.Masked,
            because: "masked tool results must be marked accordingly");
        compactedMessages[^1].Should().BeSameAs(last,
            because: "the newest message should remain in the protected window");
        compactedMessages[^2].Should().BeSameAs(secondToLast,
            because: "recent messages should remain untouched by compaction");

        var compactedTokenCount = counter.Count(compactedMessages);
        compactedTokenCount.Should().BeLessThan(budget.CompactionTriggerTokens,
            because: "compaction should reduce the prepared context below the configured threshold");
    }

    [Fact]
    public async Task PrepareAsync_WhenProviderInputTokensAreRecordedAfterCompaction_ReturnsCompactedViewAgain()
    {
        // Arrange
        var budget = new ContextBudget(maxTokens: 1000, compactionThreshold: 0.80);
        var counter = new EstimatedTokenCounter();
        var strategy = new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 2, protectedWindowFraction: 0.5));
        var engine = new ConversationContext(budget, counter, strategy);
        engine.SetSystemPrompt("You are a helpful assistant.");
        engine.AddUserMessage("Please analyze the logs for the last 24 hours.");
        engine.RecordModelResponse([new ToolUseContent("call_123", "analyze_logs", "{\"timespan\":\"24h\"}")]);
        engine.RecordToolResult("call_123", "analyze_logs", new string('A', 4000));
        engine.RecordModelResponse([new TextContent("The logs show that the system was running normally, but there was a spike in memory usage at 3 AM.")]);
        engine.AddUserMessage("Can you check the database logs around 3 AM?");
        await engine.PrepareAsync();

        int reportedInputTokens = 300;

        // Act
        engine.RecordModelResponse(
            [new ToolUseContent("call_456", "check_db", "{\"time\":\"03:00\"}")],
            providerInputTokens: reportedInputTokens);

        var finalPrepared = await engine.PrepareAsync();

        // Assert
        finalPrepared.Should().NotBeSameAs(engine.History,
            because: "provider-reported input tokens should keep the prepared view in compacted mode when the history remains over budget");
    }

    [Fact]
    public async Task PrepareAsync_WhenConversationNeedsMultipleCompactionPasses_PreservesGuaranteedProtectedTailAndMasksOnlyOlderToolResults()
    {
        // Arrange
        var budget = new ContextBudget(maxTokens: 500, compactionThreshold: 0.80);
        var counter = new EstimatedTokenCounter();
        var strategy = new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 3, protectedWindowFraction: 0.5));
        var engine = new ConversationContext(budget, counter, strategy);

        engine.AddUserMessage("Scan the directory for large files.");
        engine.RecordModelResponse([new ToolUseContent("call_1", "scan_dir", "{}")]);
        engine.RecordToolResult("call_1", "scan_dir", new string('F', 1200));
        engine.RecordModelResponse([new TextContent("Found 10 large files.")]);

        var currentCount = counter.Count(engine.History);
        currentCount.Should().BeLessThan(budget.CompactionTriggerTokens,
            because: "the first turn alone should still fit within the compaction threshold");

        // Act
        var prep1 = await engine.PrepareAsync();
        prep1.Should().Equal(engine.History,
            because: "preparing an under-budget conversation should preserve the original message sequence without compaction");
        prep1.Should().OnlyContain(message => message.State == CompactionState.Original,
            because: "an under-budget conversation should not mask or otherwise compact any messages");

        engine.AddUserMessage("Can you delete them?");
        engine.RecordModelResponse(
            [new ToolUseContent("call_2", "delete_files", "{}")],
            providerInputTokens: 330);
        engine.RecordToolResult("call_2", "delete_files", new string('D', 1200));
        engine.RecordModelResponse([new TextContent("Deleted all 10 files.")]);

        var prep2 = await engine.PrepareAsync();
        prep2.Should().NotBeSameAs(engine.History,
            because: "preparing an over-budget conversation should return a compacted projection");

        var maskedCount = prep2.Count(m => m.State == CompactionState.Masked);
        maskedCount.Should().Be(1,
            because: "the guaranteed protected tail should keep the recent oversized tool result intact while masking older tool output outside the window");

        engine.AddUserMessage("Thanks, what's next?");

        var prep3 = await engine.PrepareAsync();
        prep3.Should().NotBeSameAs(engine.History,
            because: "the conversation should still require compaction after the third user turn");

        var finalMaskedCount = prep3.Count(m => m.State == CompactionState.Masked);
        finalMaskedCount.Should().Be(1,
            because: "adding a small follow-up should not unmask previously compacted older tool results or force the protected recent tail to be masked");

        prep3[^1].Should().BeSameAs(engine.History[^1],
            because: "the latest user message should remain protected when it fits inside the window");
    }

    [Fact]
    public async Task PrepareAsync_WhenMaskedHistoryStillExceedsEmergencyThreshold_DropsOldestMessagesAndPreservesNewestTail()
    {
        // Arrange
        var budget = new ContextBudget(maxTokens: 500, compactionThreshold: 0.60, emergencyThreshold: 0.75);
        var counter = new EstimatedTokenCounter();
        var strategy = new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 1, protectedWindowFraction: 0.20));
        var engine = new ConversationContext(budget, counter, strategy);

        engine.SetSystemPrompt(new string('S', 1200));
        engine.AddUserMessage(new string('A', 1400));
        engine.RecordModelResponse([new ToolUseContent("call_1", "read_logs", "{}")]);
        engine.RecordToolResult("call_1", "read_logs", new string('B', 2500));
        engine.AddUserMessage(new string('C', 1600));
        engine.RecordModelResponse([new TextContent(new string('D', 1600))]);

        var systemMessage = engine.History[0];
        var latestUser = engine.History[^2];
        var latestModel = engine.History[^1];

        // Act
        var prepared = await engine.PrepareAsync();

        // Assert
        prepared.Should().NotBeSameAs(engine.History,
            because: "an over-budget conversation should return a prepared projection");
        prepared.Should().ContainInOrder(systemMessage, latestUser, latestModel);
        prepared.Should().HaveCount(3,
            because: "emergency truncation should remove all older non-system messages before the preserved tail");
        prepared.Should().OnlyContain(message =>
                ReferenceEquals(message, systemMessage) || ReferenceEquals(message, latestUser) || ReferenceEquals(message, latestModel),
            because: "only the system prompt and newest user-model tail should survive the emergency floor");
        counter.Count(prepared).Should().BeGreaterThan(budget.EmergencyTriggerTokens,
            because: "the preserved floor can legitimately remain over budget when it cannot be reduced further");
    }

    [Fact]
    public async Task PrepareAsync_WhenPreparedListAlreadyEqualsPreservedFloor_DoesNotDropAnything()
    {
        // Arrange
        var budget = new ContextBudget(maxTokens: 500, compactionThreshold: 0.60, emergencyThreshold: 0.75);
        var counter = new EstimatedTokenCounter();
        var strategy = new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 1, protectedWindowFraction: 0.20));
        var engine = new ConversationContext(budget, counter, strategy);

        engine.AddUserMessage(new string('U', 1800));
        engine.RecordModelResponse([new TextContent(new string('M', 1800))]);

        var latestUser = engine.History[0];
        var latestModel = engine.History[1];

        // Act
        var prepared = await engine.PrepareAsync();

        // Assert
        prepared.Should().HaveCount(2);
        prepared.Should().ContainInOrder(latestUser, latestModel);
        prepared.Should().BeEquivalentTo(engine.History,
            options => options.WithStrictOrdering(),
            "there is nothing older than the floor to truncate");
        counter.Count(prepared).Should().BeGreaterThan(budget.EmergencyTriggerTokens,
            because: "the preserved floor may still exceed the emergency threshold");
    }

    [Fact]
    public async Task PrepareAsync_WhenOnlyNewestUserRemainsDroppable_DoesNotDropFinalUserMessage()
    {
        // Arrange
        var budget = new ContextBudget(maxTokens: 500, compactionThreshold: 0.60, emergencyThreshold: 0.75);
        var counter = new EstimatedTokenCounter();
        var strategy = new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 1, protectedWindowFraction: 0.20));
        var engine = new ConversationContext(budget, counter, strategy);

        engine.SetSystemPrompt(new string('S', 1200));
        engine.AddUserMessage(new string('O', 1400));
        engine.AddUserMessage(new string('N', 2200));

        var systemMessage = engine.History[0];
        var latestUser = engine.History[^1];

        // Act
        var prepared = await engine.PrepareAsync();

        // Assert
        prepared.Should().HaveCount(2);
        prepared.Should().ContainInOrder(systemMessage, latestUser);
        prepared.Should().NotContain(engine.History[1],
            because: "older non-system messages should be dropped before the preserved floor");
        counter.Count(prepared).Should().BeGreaterThan(budget.EmergencyTriggerTokens,
            because: "the last user message may still leave the conversation over the emergency threshold");
    }

    [Fact]
    public async Task PrepareAsync_WhenPreparedHistoryHasNoDroppableMessagesBeforeFloor_ReturnsPreparedHistoryUnchanged()
    {
        // Arrange
        var budget = new ContextBudget(maxTokens: 700, compactionThreshold: 0.60, emergencyThreshold: 0.75);
        var counter = new EstimatedTokenCounter();
        var strategy = new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 1, protectedWindowFraction: 0.20));
        var engine = new ConversationContext(budget, counter, strategy);

        engine.SetSystemPrompt(new string('S', 1800));
        engine.SetSystemPrompt(new string('T', 1800));
        engine.AddUserMessage(new string('U', 2200));

        var latestUser = engine.History[^1];
        var expectedPrepared = engine.History.ToArray();

        // Act
        var prepared = await engine.PrepareAsync();

        // Assert
        prepared.Should().BeEquivalentTo(expectedPrepared,
            options => options.WithStrictOrdering(),
            "messages before the preserved floor are system messages only and are never eligible for emergency truncation");
        prepared.Should().OnlyContain(message => message.Role == MessageRole.System || ReferenceEquals(message, latestUser));
        counter.Count(prepared).Should().BeGreaterThan(budget.EmergencyTriggerTokens,
            because: "the unchanged preserved floor plus system messages can remain over the emergency threshold");
    }
}
