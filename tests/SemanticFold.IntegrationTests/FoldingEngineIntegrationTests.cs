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

        var messages = new List<Message>();

        // System Prompt (using User role)
        var systemMessage = Message.FromText(MessageRole.User, "You are a helpful assistant.");
        messages.Add(systemMessage);
        
        var prepared = engine.Prepare(messages);
        Assert.Same(messages, prepared); 

        // User makes a request
        var userRequest = Message.FromText(MessageRole.User, "Please analyze the logs for the last 24 hours.");
        messages.Add(userRequest);
        
        // Assistant calls a tool
        var toolUse = new ToolUseContent("call_123", "analyze_logs", "{\"timespan\":\"24h\"}");
        var assistantToolCall = Message.FromContent(MessageRole.Model, new ContentBlock[] { toolUse });
        messages.Add(assistantToolCall);
        
        engine.Observe(assistantToolCall); 

        // Tool responds with a massive log (~1000 tokens)
        var massiveLog = new string('A', 4000); 
        var toolResult = new ToolResultContent("call_123", "analyze_logs", massiveLog);
        var toolResultMessage = Message.FromContent(MessageRole.Tool, new ContentBlock[] { toolResult });
        messages.Add(toolResultMessage);
        
        engine.Observe(toolResultMessage); 

        // Assistant responds
        var assistantResponse1 = Message.FromText(MessageRole.Model, "The logs show that the system was running normally, but there was a spike in memory usage at 3 AM.");
        messages.Add(assistantResponse1);
        engine.Observe(assistantResponse1);
        
        // User asks a follow up
        var userFollowUp = Message.FromText(MessageRole.User, "Can you check the database logs around 3 AM?");
        messages.Add(userFollowUp);
        
        // Now we prepare for the next turn. 
        var compactedMessages = engine.Prepare(messages);

        Assert.NotSame(messages, compactedMessages);
        
        // Verify tool result masking
        var compactedToolResult = compactedMessages.FirstOrDefault(m => 
            m.Role == MessageRole.Tool && 
            m.Content.Any(c => c is TextContent tc && tc.Text.Contains("[Tool result cleared —", StringComparison.OrdinalIgnoreCase)));
            
        Assert.NotNull(compactedToolResult);
        Assert.Equal(CompactionState.Masked, compactedToolResult.State);

        // Verify window protection
        Assert.Same(messages[^1], compactedMessages[^1]); 
        Assert.Same(messages[^2], compactedMessages[^2]); 
        
        var compactedTokenCount = counter.Count(compactedMessages);
        Assert.True(compactedTokenCount < budget.CompactionTriggerTokens, 
            $"Compacted tokens ({compactedTokenCount}) should be less than the threshold ({budget.CompactionTriggerTokens})");
            
        // Test anchor correction
        int reportedInputTokens = 300;
        
        var assistantToolCall2 = Message.FromContent(MessageRole.Model, new ContentBlock[] { new ToolUseContent("call_456", "check_db", "{\"time\":\"03:00\"}") });
        messages.Add(assistantToolCall2);
        
        engine.Observe(assistantToolCall2, apiReportedInputTokens: reportedInputTokens);
        
        var finalPrepared = engine.Prepare(messages);
        Assert.NotSame(messages, finalPrepared);
        Assert.Equal(messages.Count, finalPrepared.Count);
    }

    [Fact]
    public void FullConversationLifecycle_WithMultipleCompactionPasses_WorksCorrectly()
    {
        var budget = new ContextBudget(maxTokens: 500, compactionThreshold: 0.80); // trigger ~400
        var counter = new EstimatedTokenCounter();
        var strategy = new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 3, protectedWindowFraction: 0.5));
        var engine = new FoldingEngine(budget, counter, strategy);

        var messages = new List<Message>();

        // Turn 1
        messages.Add(Message.FromText(MessageRole.User, "Scan the directory for large files."));
        var toolUse1 = new ToolUseContent("call_1", "scan_dir", "{}");
        var assistantMsg1 = Message.FromContent(MessageRole.Model, new ContentBlock[] { toolUse1 });
        messages.Add(assistantMsg1);
        engine.Observe(assistantMsg1);

        var largeToolResult1 = new ToolResultContent("call_1", "scan_dir", new string('F', 1200)); // ~300 tokens
        var toolMsg1 = Message.FromContent(MessageRole.Tool, new ContentBlock[] { largeToolResult1 });
        messages.Add(toolMsg1);
        engine.Observe(toolMsg1);

        var asstResponse1 = Message.FromText(MessageRole.Model, "Found 10 large files.");
        messages.Add(asstResponse1);
        engine.Observe(asstResponse1);

        // Should not be compacted yet, total ~320 tokens < 400
        var currentCount = counter.Count(messages);
        Assert.True(currentCount < budget.CompactionTriggerTokens, $"Expected count {currentCount} to be < {budget.CompactionTriggerTokens}");
        var prep1 = engine.Prepare(messages);
        Assert.Same(messages, prep1);

        // Turn 2
        messages.Add(Message.FromText(MessageRole.User, "Can you delete them?"));
        var toolUse2 = new ToolUseContent("call_2", "delete_files", "{}");
        var assistantMsg2 = Message.FromContent(MessageRole.Model, new ContentBlock[] { toolUse2 });
        messages.Add(assistantMsg2);
        engine.Observe(assistantMsg2);

        // API reported token count for turn 2 request (to anchor)
        engine.Observe(assistantMsg2, apiReportedInputTokens: 330);

        var largeToolResult2 = new ToolResultContent("call_2", "delete_files", new string('D', 1200)); // ~300 tokens
        var toolMsg2 = Message.FromContent(MessageRole.Tool, new ContentBlock[] { largeToolResult2 });
        messages.Add(toolMsg2);
        engine.Observe(toolMsg2);

        var asstResponse2 = Message.FromText(MessageRole.Model, "Deleted all 10 files.");
        messages.Add(asstResponse2);
        engine.Observe(asstResponse2);

        // Now total is ~650 tokens > 400 threshold
        var prep2 = engine.Prepare(messages);
        Assert.NotSame(messages, prep2);

        // First tool result should be masked, second should possibly be masked depending on protected window
        // The protected tokens = 500 * 0.5 = 250. 
        // Window size = 3 messages. Newest: asstResponse2, toolMsg2, assistantMsg2. 
        // toolMsg2 is ~300 tokens, which exceeds maxProtectedTokens (250). 
        // This means it will break early, protecting only the newest ones that fit (asstResponse2).
        // So toolMsg2 should ALSO be masked.
        
        var maskedCount = prep2.Count(m => m.State == CompactionState.Masked);
        Assert.Equal(2, maskedCount); // both large tool results masked

        // Turn 3
        messages.Add(Message.FromText(MessageRole.User, "Thanks, what's next?"));
        
        var prep3 = engine.Prepare(messages);
        Assert.NotSame(messages, prep3);

        var finalMaskedCount = prep3.Count(m => m.State == CompactionState.Masked);
        Assert.Equal(2, finalMaskedCount); 
        
        // Assert that the user's latest message is strictly protected (as it's small)
        Assert.Same(messages[^1], prep3[^1]);
    }
}
