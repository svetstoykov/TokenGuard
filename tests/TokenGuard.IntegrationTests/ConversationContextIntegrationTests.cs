using FluentAssertions;
using OpenAI.Chat;
using TokenGuard.Core;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Options;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Strategies;
using TokenGuard.Core.TokenCounting;
using TokenGuard.Extensions.OpenAI;

namespace TokenGuard.IntegrationTests;

public sealed class ConversationContextIntegrationTests
{
    [Fact]
    public async Task PrepareAsync_WhenLargeToolResultPushesHistoryOverThreshold_MasksOldToolResultAndPreservesRecentMessages()
    {
        // Arrange
        var budget = new ContextBudget(maxTokens: 1000, compactionThreshold: 0.80);
        var counter = new EstimatedTokenCounter();
        var strategy = new SlidingWindowStrategy(counter, new SlidingWindowOptions(windowSize: 2, protectedWindowFraction: 0.5));
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
        var result = await engine.PrepareAsync();
        var compactedMessages = result.Messages;

        // Assert
        compactedMessages.Should().NotBeSameAs(engine.History,
            because: "preparing an over-budget conversation should return a compacted view");

        var compactedToolResult = compactedMessages.FirstOrDefault(m =>
            m.Role == MessageRole.Tool &&
            m.Segments.Any(c => c is ToolResultContent tc && tc.Content.Contains("[Tool result cleared", StringComparison.OrdinalIgnoreCase)));

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
        var strategy = new SlidingWindowStrategy(counter, new SlidingWindowOptions(windowSize: 2, protectedWindowFraction: 0.5));
        var engine = new ConversationContext(budget, counter, strategy);
        engine.SetSystemPrompt("You are a helpful assistant.");
        engine.AddUserMessage("Please analyze the logs for the last 24 hours.");
        engine.RecordModelResponse([new ToolUseContent("call_123", "analyze_logs", "{\"timespan\":\"24h\"}")]);
        engine.RecordToolResult("call_123", "analyze_logs", new string('A', 4000));
        engine.RecordModelResponse([new TextContent("The logs show that the system was running normally, but there was a spike in memory usage at 3 AM.")]);
        engine.AddUserMessage("Can you check the database logs around 3 AM?");
        var _ = await engine.PrepareAsync();

        int reportedInputTokens = 300;

        // Act
        engine.RecordModelResponse(
            [new ToolUseContent("call_456", "check_db", "{\"time\":\"03:00\"}")],
            providerInputTokens: reportedInputTokens);

        var finalResult = await engine.PrepareAsync();
        var finalPrepared = finalResult.Messages;

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
        var strategy = new SlidingWindowStrategy(counter, new SlidingWindowOptions(windowSize: 3, protectedWindowFraction: 0.5));
        var engine = new ConversationContext(budget, counter, strategy);

        engine.AddUserMessage("Scan the directory for large files.");
        engine.RecordModelResponse([new ToolUseContent("call_1", "scan_dir", "{}")]);
        engine.RecordToolResult("call_1", "scan_dir", new string('F', 1200));
        engine.RecordModelResponse([new TextContent("Found 10 large files.")]);

        var currentCount = counter.Count(engine.History);
        currentCount.Should().BeLessThan(budget.CompactionTriggerTokens,
            because: "the first turn alone should still fit within the compaction threshold");

        // Act
        var prep1Result = await engine.PrepareAsync();
        var prep1 = prep1Result.Messages;
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

        var prep2Result = await engine.PrepareAsync();
        var prep2 = prep2Result.Messages;
        prep2.Should().NotBeSameAs(engine.History,
            because: "preparing an over-budget conversation should return a compacted projection");

        var maskedCount = prep2.Count(m => m.State == CompactionState.Masked);
        maskedCount.Should().Be(1,
            because: "the guaranteed pro   tected tail should keep the recent oversized tool result intact while masking older tool output outside the window");

        engine.AddUserMessage("Thanks, what's next?");

        var prep3Result = await engine.PrepareAsync();
        var prep3 = prep3Result.Messages;
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
        var strategy = new SlidingWindowStrategy(counter, new SlidingWindowOptions(windowSize: 1, protectedWindowFraction: 0.20));
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
        var result = await engine.PrepareAsync();
        var prepared = result.Messages;

        // Assert
        prepared.Should().NotBeSameAs(engine.History,
            because: "an over-budget conversation should return a prepared projection");
        prepared.Should().ContainInOrder(systemMessage, latestUser, latestModel);
        prepared.Should().HaveCount(3,
            because: "emergency truncation should remove all older non-system messages before the preserved tail");
        prepared.Should().OnlyContain(message =>
                ReferenceEquals(message, systemMessage) || ReferenceEquals(message, latestUser) || ReferenceEquals(message, latestModel),
            because: "only the system prompt and newest user-model tail should survive the emergency floor");
        counter.Count(prepared).Should().BeGreaterThan(budget.EmergencyTriggerTokens!.Value,
            because: "the preserved floor can legitimately remain over budget when it cannot be reduced further");
    }

    [Fact]
    public async Task PrepareAsync_WhenPreparedListAlreadyEqualsPreservedFloor_DoesNotDropAnything()
    {
        // Arrange
        var budget = new ContextBudget(maxTokens: 500, compactionThreshold: 0.60, emergencyThreshold: 0.75);
        var counter = new EstimatedTokenCounter();
        var strategy = new SlidingWindowStrategy(counter, new SlidingWindowOptions(windowSize: 1, protectedWindowFraction: 0.20));
        var engine = new ConversationContext(budget, counter, strategy);

        engine.AddUserMessage(new string('U', 1800));
        engine.RecordModelResponse([new TextContent(new string('M', 1800))]);

        var latestUser = engine.History[0];
        var latestModel = engine.History[1];

        // Act
        var result = await engine.PrepareAsync();
        var prepared = result.Messages;

        // Assert
        prepared.Should().HaveCount(2);
        prepared.Should().ContainInOrder(latestUser, latestModel);
        prepared.Should().BeEquivalentTo(engine.History,
            options => options.WithStrictOrdering(),
            "there is nothing older than the floor to truncate");
        counter.Count(prepared).Should().BeGreaterThan(budget.EmergencyTriggerTokens!.Value,
            because: "the preserved floor may still exceed the emergency threshold");
    }

    [Fact]
    public async Task PrepareAsync_WhenOnlyNewestUserRemainsDroppable_DoesNotDropFinalUserMessage()
    {
        // Arrange
        var budget = new ContextBudget(maxTokens: 500, compactionThreshold: 0.60, emergencyThreshold: 0.75);
        var counter = new EstimatedTokenCounter();
        var strategy = new SlidingWindowStrategy(counter, new SlidingWindowOptions(windowSize: 1, protectedWindowFraction: 0.20));
        var engine = new ConversationContext(budget, counter, strategy);

        engine.SetSystemPrompt(new string('S', 1200));
        engine.AddUserMessage(new string('O', 1400));
        engine.AddUserMessage(new string('N', 2200));

        var systemMessage = engine.History[0];
        var latestUser = engine.History[^1];

        // Act
        var result = await engine.PrepareAsync();
        var prepared = result.Messages;

        // Assert
        prepared.Should().HaveCount(2);
        prepared.Should().ContainInOrder(systemMessage, latestUser);
        prepared.Should().NotContain(engine.History[1],
            because: "older non-system messages should be dropped before the preserved floor");
        counter.Count(prepared).Should().BeGreaterThan(budget.EmergencyTriggerTokens!.Value,
            because: "the last user message may still leave the conversation over the emergency threshold");
    }

    [Fact]
    public async Task PrepareAsync_WhenPreparedHistoryHasNoDroppableMessagesBeforeFloor_ReturnsPreparedHistoryUnchanged()
    {
        // Arrange
        var budget = new ContextBudget(maxTokens: 700, compactionThreshold: 0.60, emergencyThreshold: 0.75);
        var counter = new EstimatedTokenCounter();
        var strategy = new SlidingWindowStrategy(counter, new SlidingWindowOptions(windowSize: 1, protectedWindowFraction: 0.20));
        var engine = new ConversationContext(budget, counter, strategy);

        engine.SetSystemPrompt(new string('S', 1800));
        engine.SetSystemPrompt(new string('T', 1800));
        engine.AddUserMessage(new string('U', 2200));

        var latestUser = engine.History[^1];
        var expectedPrepared = engine.History.ToArray();

        // Act
        var result = await engine.PrepareAsync();
        var prepared = result.Messages;

        // Assert
        prepared.Should().BeEquivalentTo(expectedPrepared,
            options => options.WithStrictOrdering(),
            "messages before the preserved floor are system messages only and are never eligible for emergency truncation");
        prepared.Should().OnlyContain(message => message.Role == MessageRole.System || ReferenceEquals(message, latestUser));
        counter.Count(prepared).Should().BeGreaterThan(budget.EmergencyTriggerTokens!.Value,
            because: "the unchanged preserved floor plus system messages can remain over the emergency threshold");
    }

    [Fact]
    public async Task PrepareAsync_WhenEmergencyTruncationDropsModelTurn_AlsoDropsAssociatedToolResult()
    {
        // Arrange
        // Current EstimatedTokenCounter math:
        //   model_1 = ToolUseContent("call_1","analyze",largeArgs[86 chars]) → 36 T
        //   tool_1  = ToolResultContent("call_1","analyze",2000 chars)        → very large original → masked to 31 T
        //   model_2 = TextContent(184 chars)                                  → 41 T
        //   user    = "Continue the process please."                          → 9 T   [protected tail]
        //   model_3 = TextContent(636 chars)                                  → 132 T [protected tail]
        //
        // compaction trigger = floor(500 × 0.48) = 240 T
        // emergency trigger  = floor(500 × 0.49) = 245 T
        //
        // original total remains well above trigger; SlidingWindow masks tool_1 → total = 249 T → emergency fires
        //
        // old (buggy): drop model_1 alone (36 T) → 249-36 = 213 ≤ 245 → stop — tool_1_masked at index 0 is orphaned → HTTP 400
        // new (fixed): drop {model_1, tool_1_masked} together (67 T) → 249-67 = 182 ≤ 245 → stop — no orphan
        var budget = new ContextBudget(maxTokens: 500, compactionThreshold: 0.48, emergencyThreshold: 0.49);
        var counter = new EstimatedTokenCounter();
        var strategy = new SlidingWindowStrategy(counter, new SlidingWindowOptions(windowSize: 2, protectedWindowFraction: 0.10));
        var engine = new ConversationContext(budget, counter, strategy);

        var largeArgs = $"{{\"query\": \"{new string('A', 70)}\"}}";
        engine.RecordModelResponse([new ToolUseContent("call_1", "analyze", largeArgs)]);
        engine.RecordToolResult("call_1", "analyze", new string('X', 2000));
        engine.RecordModelResponse([new TextContent(new string('M', 184))]);
        engine.AddUserMessage("Continue the process please.");
        engine.RecordModelResponse([new TextContent(new string('N', 636))]);

        var turn1Model = engine.History[0];

        // Act
        var result = await engine.PrepareAsync();
        var prepared = result.Messages;

        // Assert
        for (var i = 0; i < prepared.Count; i++)
        {
            if (prepared[i].Role != MessageRole.Tool) continue;
            i.Should().BeGreaterThan(0,
                because: "a Tool message must never be the first message in the prepared list");
            prepared[i - 1].Role.Should().Be(MessageRole.Model,
                because: "every tool result must be immediately preceded by its originating model turn");
        }

        prepared.Should().NotContain(m => ReferenceEquals(m, turn1Model),
            because: "the model turn that issued tool call_1 is outside the protected window and must be removed by emergency truncation");

        prepared.Should().NotContain(m =>
                m.Role == MessageRole.Tool &&
                m.Segments.OfType<ToolResultContent>().Any(r => r.ToolCallId == "call_1"),
            because: "the tool result paired with the dropped model turn must be removed atomically — leaving it orphaned produces a malformed message sequence that providers reject with HTTP 400");
    }

    [Fact]
    public async Task PrepareAsync_WhenEmergencyTruncationHitsTrailingToolResult_PreservesAssistantToolCallPair()
    {
        // Arrange
        // The tool-call arguments are intentionally large enough that dropping only the model message
        // would satisfy the emergency budget and expose the orphaned tool-result bug.
        var budget = new ContextBudget(maxTokens: 120, compactionThreshold: 0.50, emergencyThreshold: 0.60);
        var counter = new EstimatedTokenCounter();
        var strategy = new SlidingWindowStrategy(counter, new SlidingWindowOptions(windowSize: 1, protectedWindowFraction: 0.10));
        var engine = new ConversationContext(budget, counter, strategy);

        engine.RecordModelResponse([new ToolUseContent("call_1", "search", $"{{\"query\":\"{new string('Q', 600)}\"}}")]);
        engine.RecordToolResult("call_1", "search", "ok");

        // Act
        var result = await engine.PrepareAsync();
        var prepared = result.Messages;
        var openAiMessages = prepared.ForOpenAI();

        // Assert
        prepared.Should().HaveCount(2,
            because: "the preserved emergency floor must widen to keep the assistant tool call paired with its tool result");
        prepared[0].Role.Should().Be(MessageRole.Model);
        prepared[1].Role.Should().Be(MessageRole.Tool);

        openAiMessages.Should().HaveCount(2);
        openAiMessages[0].Should().BeOfType<AssistantChatMessage>();
        openAiMessages[1].Should().BeOfType<ToolChatMessage>();
        openAiMessages[1].As<ToolChatMessage>().ToolCallId.Should().Be("call_1");
    }

    [Fact]
    public async Task PrepareAsync_WhenEmergencyThresholdIsNotConfigured_DoesNotDropMessagesAfterCompaction()
    {
        // Arrange
        // No emergency threshold — emergency truncation must not run even when the strategy result is over budget.
        // compactionTrigger = floor(500 × 0.60) = 300 T. SlidingWindow keeps only the newest turn, so most history
        // is masked. The masked result still exceeds what an emergency threshold would have been, but because no
        // threshold is configured the runtime must not drop any further messages.
        var budget = new ContextBudget(maxTokens: 500, compactionThreshold: 0.60);
        var counter = new EstimatedTokenCounter();
        var strategy = new SlidingWindowStrategy(counter, new SlidingWindowOptions(windowSize: 1, protectedWindowFraction: 0.20));
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
        var result = await engine.PrepareAsync();
        var prepared = result.Messages;

        // Assert
        prepared.Should().Contain(m => ReferenceEquals(m, systemMessage),
            because: "the pinned system prompt must always survive compaction");
        prepared.Should().Contain(m => ReferenceEquals(m, latestUser),
            because: "the newest user turn must survive compaction");
        prepared.Should().Contain(m => ReferenceEquals(m, latestModel),
            because: "the newest model turn must survive compaction");
        result.Outcome.Should().NotBe(PrepareOutcome.Ready,
            because: "the conversation required compaction");
    }

    [Fact]
    public async Task PrepareAsync_WhenEmergencyThresholdIsConfigured_DropsOldestMessagesWhenStrategyResultStillExceedsThreshold()
    {
        // Arrange
        // Same scenario as the no-emergency test above, but with emergencyThreshold: 0.75 added.
        // The strategy result exceeds the emergency threshold, so the runtime must drop oldest turn groups.
        var budget = new ContextBudget(maxTokens: 500, compactionThreshold: 0.60, emergencyThreshold: 0.75);
        var counter = new EstimatedTokenCounter();
        var strategy = new SlidingWindowStrategy(counter, new SlidingWindowOptions(windowSize: 1, protectedWindowFraction: 0.20));
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
        var result = await engine.PrepareAsync();
        var prepared = result.Messages;

        // Assert
        prepared.Should().ContainInOrder(systemMessage, latestUser, latestModel);
        prepared.Should().HaveCount(3,
            because: "emergency truncation must remove all older non-system messages before the preserved tail");
        prepared.Should().OnlyContain(m =>
                ReferenceEquals(m, systemMessage) || ReferenceEquals(m, latestUser) || ReferenceEquals(m, latestModel),
            because: "only the pinned system prompt and the newest user-model turn should survive the emergency pass");
    }

    [Fact]
    public async Task PrepareAsync_WhenLlmSummarizationTriggers_ReplacesOlderFlowWithSummaryAndPreservesPinnedSystemAndRecentTail()
    {
        // Arrange
        var budget = new ContextBudget(maxTokens: 100, compactionThreshold: 0.55);
        var counter = new EstimatedTokenCounter();
        var summarizer = new TrackingSummarizer("summary: initial investigation complete.");
        var strategy = new LlmSummarizationStrategy(
            summarizer,
            counter,
            new LlmSummarizationOptions(windowSize: 2, minSummaryTokens: 1, maxSummaryTokens: 100));
        var engine = new ConversationContext(budget, counter, strategy);

        engine.SetSystemPrompt("You are a careful assistant.");
        engine.AddUserMessage(new string('A', 120));
        engine.RecordModelResponse([new ToolUseContent("call_1", "read_logs", "{\"path\":\"app.log\"}")]);
        engine.RecordToolResult("call_1", "read_logs", new string('B', 120));
        engine.AddUserMessage(new string('C', 120));
        engine.RecordModelResponse([new TextContent(new string('D', 120))]);

        var systemMessage = engine.History[0];
        var summarizedPrefix = engine.History.Skip(1).Take(3).ToArray();
        var protectedTail = engine.History.Skip(engine.History.Count - 2).ToArray();
        var availableTokens = budget.MaxTokens - counter.Count(systemMessage);
        var expectedTargetTokens = Math.Min(availableTokens - counter.Count(protectedTail), 100);

        // Act
        var result = await engine.PrepareAsync();
        var prepared = result.Messages;

        // Assert
        summarizer.CallCount.Should().Be(1);
        summarizer.Calls[0].Messages.Should().HaveCount(3);
        summarizer.Calls[0].Messages[0].Should().BeSameAs(summarizedPrefix[0]);
        summarizer.Calls[0].Messages[1].Should().BeSameAs(summarizedPrefix[1]);
        summarizer.Calls[0].Messages[2].Should().BeSameAs(summarizedPrefix[2]);
        summarizer.Calls[0].TargetTokens.Should().Be(expectedTargetTokens);

        result.Outcome.Should().Be(PrepareOutcome.Compacted);
        result.MessagesCompacted.Should().Be(3);
        result.MessagesDropped.Should().Be(0);
        result.TokensAfterCompaction.Should().BeLessThan(result.TokensBeforeCompaction);

        prepared.Should().HaveCount(4);
        prepared[0].Should().BeSameAs(systemMessage);
        prepared[1].Role.Should().Be(MessageRole.Model);
        prepared[1].State.Should().Be(CompactionState.Summarized);
        prepared[1].Segments.Should().ContainSingle()
            .Which.Should().BeOfType<TextContent>()
            .Which.Content.Should().Be("summary: initial investigation complete.");
        prepared[2].Should().BeSameAs(protectedTail[0]);
        prepared[3].Should().BeSameAs(protectedTail[1]);
    }

    [Fact]
    public async Task PrepareAsync_WhenSummaryCheckpointExists_ReusesItForStableHistoryAndPromotesItWhenConversationGrows()
    {
        // Arrange
        var budget = new ContextBudget(maxTokens: 80, compactionThreshold: 0.55);
        var counter = new EstimatedTokenCounter();
        var summarizer = new TrackingSummarizer(call => Task.FromResult($"summary-{call.CallNumber}"));
        var strategy = new LlmSummarizationStrategy(
            summarizer,
            counter,
            new LlmSummarizationOptions(windowSize: 2, minSummaryTokens: 1, maxSummaryTokens: 100));
        var engine = new ConversationContext(budget, counter, strategy);

        engine.AddUserMessage(new string('A', 40));
        _ = await engine.PrepareAsync();
        engine.RecordModelResponse([new TextContent(new string('B', 40))]);
        _ = await engine.PrepareAsync();
        engine.AddUserMessage(new string('C', 40));
        _ = await engine.PrepareAsync();
        engine.RecordModelResponse([new TextContent(new string('D', 40))]);

        var firstBoundary = engine.History.Take(2).ToArray();

        // Act
        var first = await engine.PrepareAsync();
        var second = await engine.PrepareAsync();

        engine.AddUserMessage(new string('E', 40));
        var promoted = await engine.PrepareAsync();

        // Assert
        summarizer.CallCount.Should().Be(1);
        summarizer.Calls[0].Messages.Should().HaveCount(2);
        summarizer.Calls[0].Messages[0].Should().BeSameAs(firstBoundary[0]);
        summarizer.Calls[0].Messages[1].Should().BeSameAs(firstBoundary[1]);

        first.Messages.Should().HaveCount(3);
        first.Messages[0].Segments.Should().ContainSingle()
            .Which.Should().BeOfType<TextContent>()
            .Which.Content.Should().Be("summary-1");

        second.Messages.Should().HaveCount(3);
        second.Messages[0].Segments.Should().ContainSingle()
            .Which.Should().BeOfType<TextContent>()
            .Which.Content.Should().Be("summary-1");

        promoted.Outcome.Should().Be(PrepareOutcome.Compacted);
        promoted.Messages.Should().HaveCount(4);
        promoted.Messages[0].Segments.Should().ContainSingle()
            .Which.Should().BeOfType<TextContent>()
            .Which.Content.Should().Be("summary-1");
        promoted.Messages[1].Should().BeSameAs(engine.History[2]);
        promoted.Messages[2].Should().BeSameAs(engine.History[3]);
        promoted.Messages[3].Should().BeSameAs(engine.History[4]);
    }

    [Fact]
    public async Task PrepareAsync_WhenRemainingBudgetFallsBelowMinSummaryTokens_SkipsSummarizerAndLetsEmergencyTruncationDropOldestMessage()
    {
        // Arrange
        var budget = new ContextBudget(maxTokens: 45, compactionThreshold: 0.50, emergencyThreshold: 1.0);
        var counter = new EstimatedTokenCounter();
        var summarizer = new TrackingSummarizer("unused");
        var strategy = new LlmSummarizationStrategy(
            summarizer,
            counter,
            new LlmSummarizationOptions(windowSize: 2, minSummaryTokens: 25, maxSummaryTokens: 100));
        var engine = new ConversationContext(budget, counter, strategy);

        engine.AddUserMessage(new string('O', 120));
        engine.AddUserMessage(new string('K', 40));
        engine.RecordModelResponse([new TextContent(new string('M', 40))]);

        var keep1 = engine.History[1];
        var keep2 = engine.History[2];

        // Act
        var result = await engine.PrepareAsync();
        var prepared = result.Messages;

        // Assert
        summarizer.CallCount.Should().Be(0);
        result.Outcome.Should().Be(PrepareOutcome.Compacted);
        result.MessagesCompacted.Should().Be(1);
        prepared.Should().HaveCount(2);
        prepared[0].Should().BeSameAs(keep1);
        prepared[1].Should().BeSameAs(keep2);
    }

    [Fact]
    public async Task PrepareAsync_WhenSummarizedHistoryStillExceedsEmergencyThreshold_PreservesSummaryFloorAndReturnsCompactedOutcome()
    {
        // Arrange
        var budget = new ContextBudget(maxTokens: 90, compactionThreshold: 0.55, emergencyThreshold: 0.75);
        var counter = new EstimatedTokenCounter();
        var summarizer = new TrackingSummarizer(new string('S', 160));
        var strategy = new LlmSummarizationStrategy(
            summarizer,
            counter,
            new LlmSummarizationOptions(windowSize: 2, minSummaryTokens: 1, maxSummaryTokens: 100));
        var engine = new ConversationContext(budget, counter, strategy);

        engine.AddUserMessage(new string('A', 120));
        engine.RecordModelResponse([new TextContent(new string('B', 120))]);
        engine.AddUserMessage(new string('C', 60));
        engine.RecordModelResponse([new TextContent(new string('D', 60))]);

        var latestUser = engine.History[^2];
        var latestModel = engine.History[^1];

        // Act
        var result = await engine.PrepareAsync();
        var prepared = result.Messages;

        // Assert
        summarizer.CallCount.Should().Be(1);
        result.Outcome.Should().Be(PrepareOutcome.Compacted);
        result.MessagesDropped.Should().Be(0,
            because: "the summarized history becomes preserved floor and emergency truncation has nothing eligible to drop");
        result.BudgetFailureReason.Should().BeNull();

        prepared.Should().HaveCount(3);
        prepared[0].State.Should().Be(CompactionState.Summarized);
        prepared[1].Should().BeSameAs(latestUser);
        prepared[2].Should().BeSameAs(latestModel);
        result.TokensAfterCompaction.Should().BeGreaterThan(budget.EmergencyTriggerTokens!.Value);
        result.TokensAfterCompaction.Should().BeLessThanOrEqualTo(budget.MaxTokens + budget.OverrunToleranceTokens);
    }

    [Fact]
    public async Task PrepareAsync_WhenSummarizationProtectsToolTurn_ForOpenAIKeepsToolResultPairedWithAssistantToolCall()
    {
        // Arrange
        var budget = new ContextBudget(maxTokens: 140, compactionThreshold: 0.55);
        var counter = new EstimatedTokenCounter();
        var summarizer = new TrackingSummarizer("summary");
        var strategy = new LlmSummarizationStrategy(
            summarizer,
            counter,
            new LlmSummarizationOptions(windowSize: 2, minSummaryTokens: 1, maxSummaryTokens: 100));
        var engine = new ConversationContext(budget, counter, strategy);

        engine.AddUserMessage(new string('A', 120));
        _ = await engine.PrepareAsync();

        engine.RecordModelResponse([new TextContent(new string('B', 120))]);
        _ = await engine.PrepareAsync();

        engine.RecordModelResponse([new ToolUseContent("call_1", "search", "{\"query\":\"token guard\"}")]);
        engine.RecordToolResult("call_1", "search", new string('C', 120));
        _ = await engine.PrepareAsync();

        engine.AddUserMessage(new string('D', 120));

        // Act
        var prepared = (await engine.PrepareAsync()).Messages;
        var openAiMessages = prepared.ForOpenAI();

        // Assert
        prepared[0].State.Should().Be(CompactionState.Summarized);
        prepared[0].Role.Should().Be(MessageRole.Model);
        prepared[1].Role.Should().Be(MessageRole.Model);
        prepared[2].Role.Should().Be(MessageRole.Tool);
        prepared[3].Role.Should().Be(MessageRole.User);

        openAiMessages[0].Should().BeOfType<OpenAI.Chat.AssistantChatMessage>();
        openAiMessages[1].Should().BeOfType<OpenAI.Chat.AssistantChatMessage>();
        openAiMessages[2].Should().BeOfType<OpenAI.Chat.ToolChatMessage>();
        openAiMessages[3].Should().BeOfType<OpenAI.Chat.UserChatMessage>();
    }

    [Fact]
    public async Task PrepareAsync_WhenSummaryOvershoots_EmergencyTruncationCanStillAct()
    {
        // Arrange
        // Current EstimatedTokenCounter math:
        //   old_user  (400 chars) = 84T
        //   old_model (200 chars) = 44T
        //   old2_user (200 chars) = 44T
        //   old2_model(100 chars) = 24T
        //   keep_user (100 chars) = 24T
        //   Total = 220T
        //
        // Budget: maxTokens=200, compactionThreshold=0.10 → trigger=20T; 220>20 → compaction fires.
        //         emergencyThreshold=0.15 → limit=30T; fallback keeps 220T raw list, so emergency must trim to floor.
        //
        // LlmSummarization windowSize=1: protectedTail=[keep_user(24T)], remainingBudget=200-24=176≥1;
        //   summarizer returns 3000-char string → summary still overshoots budget → fallback.
        //
        // Emergency: all Turn=0, floor=keep_user(idx 4), groups drop oldest-first until only keep_user remains.
        var budget = new ContextBudget(maxTokens: 200, compactionThreshold: 0.10, emergencyThreshold: 0.15);
        var counter = new EstimatedTokenCounter();
        var summarizer = new TrackingSummarizer(new string('X', 3000));
        var strategy = new LlmSummarizationStrategy(
            summarizer,
            counter,
            new LlmSummarizationOptions(windowSize: 1, minSummaryTokens: 1, maxSummaryTokens: 4096));
        var engine = new ConversationContext(budget, counter, strategy);

        engine.AddUserMessage(new string('A', 400));
        engine.RecordModelResponse([new TextContent(new string('B', 200))]);
        engine.AddUserMessage(new string('C', 200));
        engine.RecordModelResponse([new TextContent(new string('D', 100))]);
        engine.AddUserMessage(new string('E', 100));
        var keepUser = engine.History[^1];

        // Act
        var result = await engine.PrepareAsync();

        // Assert — summarizer fired but overshoot caused fallback, enabling emergency truncation to act.
        summarizer.CallCount.Should().Be(1);
        result.MessagesDropped.Should().Be(4);
        result.Messages.Should().HaveCount(1);
        result.Messages[0].Should().BeSameAs(keepUser);
        result.Messages.Should().NotContain(m => m.State == CompactionState.Summarized);
    }

    [Fact]
    public async Task PrepareAsync_WhenSummarizerThrows_DoesNotThrowAndSurfacesSummarizationError()
    {
        // Arrange
        var failure = new InvalidOperationException("provider unavailable");
        var counter = new EstimatedTokenCounter();

        // Small budget: compaction triggers at 80% of 200 = 160 tokens; masking alone cannot fit so summarization escalates.
        var budget = new ContextBudget(maxTokens: 200, compactionThreshold: 0.80);

        var throwingSummarizer = new ThrowingLlmSummarizer(failure);
        var llmStrategy = new LlmSummarizationStrategy(
            throwingSummarizer,
            counter,
            new LlmSummarizationOptions(windowSize: 2, minSummaryTokens: 1, maxSummaryTokens: 100));

        var strategy = new TieredCompactionStrategy(counter, new SlidingWindowOptions(windowSize: 1, protectedWindowFraction: 0.20), llmStrategy);
        var engine = new ConversationContext(budget, counter, strategy);

        // Drive history past the compaction trigger
        engine.SetSystemPrompt("You are a helpful assistant.");
        for (var i = 0; i < 10; i++)
        {
            engine.AddUserMessage($"Message number {i}: " + new string('x', 15));
            engine.RecordModelResponse([new TextContent($"Response {i}: " + new string('y', 15))]);
        }

        // Act — must not throw
        var result = await engine.PrepareAsync();

        // Assert
        result.SummarizationError.Should().BeSameAs(failure);
        result.Outcome.Should().NotBe(PrepareOutcome.Ready);
    }

    private sealed class TrackingSummarizer : ILlmSummarizer
    {
        private readonly Func<SummarizerCall, Task<string>> _handler;

        public TrackingSummarizer(string summary)
            : this(_ => Task.FromResult(summary))
        {
        }

        public TrackingSummarizer(Func<SummarizerCall, Task<string>> handler)
        {
            this._handler = handler;
        }

        public int CallCount => this.Calls.Count;

        public List<SummarizerCall> Calls { get; } = [];

        public async Task<string> SummarizeAsync(
            IReadOnlyList<ContextMessage> messages,
            int targetTokens,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var call = new SummarizerCall(this.Calls.Count + 1, messages.ToArray(), targetTokens);
            this.Calls.Add(call);
            return await this._handler(call);
        }
    }

    private sealed record SummarizerCall(int CallNumber, IReadOnlyList<ContextMessage> Messages, int TargetTokens);

    private sealed class ThrowingLlmSummarizer : ILlmSummarizer
    {
        private readonly Exception _toThrow;

        public ThrowingLlmSummarizer(Exception toThrow) => this._toThrow = toThrow;

        public Task<string> SummarizeAsync(
            IReadOnlyList<ContextMessage> messages,
            int targetTokens,
            CancellationToken cancellationToken = default)
            => Task.FromException<string>(this._toThrow);
    }
}
 
