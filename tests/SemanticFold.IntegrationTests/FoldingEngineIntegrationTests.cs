using SemanticFold;
using SemanticFold.Models;
using SemanticFold.Models.Content;
using SemanticFold.Enums;
using SemanticFold.Strategies;
using SemanticFold.TokenCounting;
using Xunit;

namespace SemanticFold.IntegrationTests;

public sealed class FoldingEngineIntegrationTests
{
    [Fact]
    public void AgentLoop_WithLargeContext_ShouldTriggerCompaction_AndManageTokens()
    {
        // Budget: 1000 tokens max, compact at 80% (800 tokens).
        var budget = new ContextBudget(maxTokens: 1000, compactionThreshold: 0.80);
        var counter = new EstimatedTokenCounter();
        var strategy = new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 2, protectedWindowFraction: 0.5));
        var engine = new FoldingEngine(budget, counter, strategy);

        // System prompt
        engine.SetSystemPrompt("You are a helpful assistant.");

        var prepared = engine.Prepare();
        Assert.Same(engine.History, prepared);

        // User makes a request
        engine.AddUserMessage("Please analyze the logs for the last 24 hours.");

        // Assistant calls a tool
        var toolUse = new ToolUseContent("call_123", "analyze_logs", "{\"timespan\":\"24h\"}");
        engine.RecordModelResponse([toolUse]);

        // Tool responds with a massive log (~1000 tokens)
        var massiveLog = new string('A', 4000);
        engine.RecordToolResult("call_123", "analyze_logs", massiveLog);

        // Assistant responds
        engine.RecordModelResponse([new TextContent("The logs show that the system was running normally, but there was a spike in memory usage at 3 AM.")]);

        // User asks a follow up
        engine.AddUserMessage("Can you check the database logs around 3 AM?");

        // Capture the last two messages before compaction for reference-identity checks
        var secondToLast = engine.History[^2];
        var last = engine.History[^1];

        // Now we prepare for the next turn.
        var compactedMessages = engine.Prepare();

        Assert.NotSame(engine.History, compactedMessages);

        // Verify tool result masking
        var compactedToolResult = compactedMessages.FirstOrDefault(m =>
            m.Role == MessageRole.Tool &&
            m.Content.Any(c => c is TextContent tc && tc.Text.Contains("[Tool result cleared —", StringComparison.OrdinalIgnoreCase)));

        Assert.NotNull(compactedToolResult);
        Assert.Equal(CompactionState.Masked, compactedToolResult.State);

        // Verify window protection
        Assert.Same(last, compactedMessages[^1]);
        Assert.Same(secondToLast, compactedMessages[^2]);

        var compactedTokenCount = counter.Count(compactedMessages);
        Assert.True(compactedTokenCount < budget.CompactionTriggerTokens,
            $"Compacted tokens ({compactedTokenCount}) should be less than the threshold ({budget.CompactionTriggerTokens})");

        // Test anchor correction: record another model response with provider token count
        int reportedInputTokens = 300;
        engine.RecordModelResponse(
            [new ToolUseContent("call_456", "check_db", "{\"time\":\"03:00\"}")],
            providerInputTokens: reportedInputTokens);

        var finalPrepared = engine.Prepare();
        Assert.NotSame(engine.History, finalPrepared);
    }

    [Fact]
    public void FullConversationLifecycle_WithMultipleCompactionPasses_WorksCorrectly()
    {
        var budget = new ContextBudget(maxTokens: 500, compactionThreshold: 0.80); // trigger ~400
        var counter = new EstimatedTokenCounter();
        var strategy = new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 3, protectedWindowFraction: 0.5));
        var engine = new FoldingEngine(budget, counter, strategy);

        // Turn 1
        engine.AddUserMessage("Scan the directory for large files.");
        engine.RecordModelResponse([new ToolUseContent("call_1", "scan_dir", "{}")]);
        engine.RecordToolResult("call_1", "scan_dir", new string('F', 1200)); // ~300 tokens
        engine.RecordModelResponse([new TextContent("Found 10 large files.")]);

        // Should not be compacted yet, total ~320 tokens < 400
        var currentCount = counter.Count(engine.History);
        Assert.True(currentCount < budget.CompactionTriggerTokens,
            $"Expected count {currentCount} to be < {budget.CompactionTriggerTokens}");
        var prep1 = engine.Prepare();
        Assert.Same(engine.History, prep1);

        // Turn 2
        engine.AddUserMessage("Can you delete them?");
        engine.RecordModelResponse(
            [new ToolUseContent("call_2", "delete_files", "{}")],
            providerInputTokens: 330);
        engine.RecordToolResult("call_2", "delete_files", new string('D', 1200)); // ~300 tokens
        engine.RecordModelResponse([new TextContent("Deleted all 10 files.")]);

        // Now total is ~650 tokens > 400 threshold
        var prep2 = engine.Prepare();
        Assert.NotSame(engine.History, prep2);

        // First tool result should be masked, second should possibly be masked depending on protected window.
        // The protected tokens = 500 * 0.5 = 250.
        // Window size = 3 messages. Newest: asstResponse2, toolMsg2, assistantMsg2.
        // toolMsg2 is ~300 tokens, which exceeds maxProtectedTokens (250).
        // This means it will break early, protecting only the newest ones that fit (asstResponse2).
        // So toolMsg2 should ALSO be masked.
        var maskedCount = prep2.Count(m => m.State == CompactionState.Masked);
        Assert.Equal(2, maskedCount); // both large tool results masked

        // Turn 3
        engine.AddUserMessage("Thanks, what's next?");

        var prep3 = engine.Prepare();
        Assert.NotSame(engine.History, prep3);

        var finalMaskedCount = prep3.Count(m => m.State == CompactionState.Masked);
        Assert.Equal(2, finalMaskedCount);

        // Assert that the user's latest message is strictly protected (as it's small)
        Assert.Same(engine.History[^1], prep3[^1]);
    }
}
