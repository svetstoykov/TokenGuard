using FluentAssertions;
using TokenGuard.Core;
using TokenGuard.Core.Options;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Strategies;
using TokenGuard.Core.TokenCounting;
using Xunit;

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
    public async Task PrepareAsync_WhenConversationNeedsMultipleCompactionPasses_MasksOnlyTheLargeUnprotectedToolResults()
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
        prep1.Should().BeSameAs(engine.History,
            because: "preparing an under-budget conversation should not create a compacted copy");

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
        maskedCount.Should().Be(2,
            because: "both oversized tool results fall outside the protected window and should be masked");

        engine.AddUserMessage("Thanks, what's next?");

        var prep3 = await engine.PrepareAsync();
        prep3.Should().NotBeSameAs(engine.History,
            because: "the conversation should still require compaction after the third user turn");

        var finalMaskedCount = prep3.Count(m => m.State == CompactionState.Masked);
        finalMaskedCount.Should().Be(2,
            because: "adding a small follow-up should not unmask previously compacted tool results");

        prep3[^1].Should().BeSameAs(engine.History[^1],
            because: "the latest user message should remain protected when it fits inside the window");
    }
}
