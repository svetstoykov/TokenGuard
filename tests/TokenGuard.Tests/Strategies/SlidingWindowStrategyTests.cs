using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;
using TokenGuard.Core.Options;
using TokenGuard.Core.Strategies;
using TokenGuard.Core.TokenCounting;
using TokenGuard.Tests.Diagnostics;

namespace TokenGuard.Tests.Strategies;

public sealed class SlidingWindowStrategyTests
{
    private const int GrowthAvailableTokens = 1_000;
    private const int GrowthHighMark = 800;
    private const int GrowthStepTokens = 200;
    private const int GrowthExchanges = 60;
    private const int ExchangeTokens = 50;

    [Fact]
    public async Task CompactAsync_WithEmptyMessageList_ReturnsEmptyList()
    {
        // Arrange
        IReadOnlyList<ContextMessage> messages = [];
        var tokenCounter = new TrackingTokenCounter();
        var strategy = new SlidingWindowStrategy(tokenCounter);

        // Act
        var compacted = await strategy.CompactAsync(messages, 100);

        // Assert
        Assert.Same(messages, compacted.Messages);
        Assert.Empty(compacted.Messages);
        Assert.Equal(0, compacted.TokensBefore);
        Assert.Equal(0, compacted.TokensAfter);
        Assert.Equal(0, compacted.MessagesAffected);
        Assert.Equal(nameof(SlidingWindowStrategy), compacted.StrategyName);
        Assert.Equal(0, tokenCounter.CountCalls);
    }

    [Fact]
    public async Task CompactAsync_WhenAllMessagesFitWithinWindowAndTokenCap_ReturnsOriginalListReference()
    {
        // Arrange
        var messages = new List<ContextMessage>
        {
            ContextMessage.FromText(MessageRole.User, "one"),
            ContextMessage.FromText(MessageRole.Model, "two"),
        };

        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(messages[1], 2);
        tokenCounter.Set(messages[0], 2);

        var strategy = new SlidingWindowStrategy(tokenCounter, new SlidingWindowOptions(windowSize: 10, protectedWindowFraction: 0.90));

        // Act
        var compacted = await strategy.CompactAsync(messages, 100);

        // Assert
        Assert.Same(messages, compacted.Messages);
        Assert.Equal(4, compacted.TokensBefore);
        Assert.Equal(4, compacted.TokensAfter);
        Assert.Equal(0, compacted.MessagesAffected);
    }

    [Fact]
    public async Task CompactAsync_WhenAllMessagesFitInsideProtectedTokenAllowance_DoesNotCompactEvenIfCountExceedsWindowSize()
    {
        // Arrange
        var messages = new List<ContextMessage>
        {
            CreateToolResultMessage("call_0", "tool-0", "payload-0"),
            ContextMessage.FromText(MessageRole.User, "m1"),
            ContextMessage.FromText(MessageRole.User, "m2"),
            CreateToolResultMessage("call_3", "tool-3", "payload-3"),
            CreateToolResultMessage("call_4", "tool-4", "payload-4"),
        };

        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(messages[0], 1);
        tokenCounter.Set(messages[1], 1);
        tokenCounter.Set(messages[2], 1);
        tokenCounter.Set(messages[3], 1);
        tokenCounter.Set(messages[4], 4);
        tokenCounter.Set(messages[3], 2);

        var strategy = new SlidingWindowStrategy(tokenCounter, new SlidingWindowOptions(windowSize: 2, protectedWindowFraction: 0.90));

        // Act
        var compacted = await strategy.CompactAsync(messages, 10);

        // Assert
        Assert.Same(messages, compacted.Messages);
        Assert.Equal(5, tokenCounter.CountCalls);
        Assert.True(tokenCounter.WasCounted(messages[2]));
        Assert.True(tokenCounter.WasCounted(messages[0]));
        Assert.Equal(9, compacted.TokensBefore);
        Assert.Equal(9, compacted.TokensAfter);
        Assert.Equal(0, compacted.MessagesAffected);
    }

    [Fact]
    public async Task CompactAsync_WhenTokenAllowanceExceedsWindowFloor_ProtectsAsManyMessagesAsBudgetAllows()
    {
        // Arrange
        var messages = new List<ContextMessage>
        {
            CreateToolResultMessage("call_0", "tool-0", "payload-0"),
            CreateToolResultMessage("call_1", "tool-1", "payload-1"),
            CreateToolResultMessage("call_2", "tool-2", "payload-2"),
            CreateToolResultMessage("call_3", "tool-3", "payload-3"),
            CreateToolResultMessage("call_4", "tool-4", "payload-4"),
        };

        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(messages[4], 1);
        tokenCounter.Set(messages[3], 1);
        tokenCounter.Set(messages[2], 1);
        tokenCounter.Set(messages[1], 1);
        tokenCounter.Set(messages[0], 7);

        var strategy = new SlidingWindowStrategy(tokenCounter, new SlidingWindowOptions(windowSize: 2, protectedWindowFraction: 0.50));

        // Act
        var compacted = await strategy.CompactAsync(messages, 10);

        // Assert
        Assert.Equal(11, tokenCounter.CountCalls);
        Assert.Same(messages[1], compacted.Messages[1]);
        Assert.Same(messages[2], compacted.Messages[2]);
        Assert.Same(messages[3], compacted.Messages[3]);
        Assert.Same(messages[4], compacted.Messages[4]);
        Assert.Equal(CompactionState.Masked, compacted.Messages[0].State);
        Assert.Equal(11, compacted.TokensBefore);
        Assert.Equal(5, compacted.TokensAfter);
        Assert.Equal(1, compacted.MessagesAffected);
    }

    [Fact]
    public async Task CompactAsync_WhenTokenCapWouldProtectFewerThanWindowSizeMessages_StillProtectsWindowSizeFloor()
    {
        // Arrange
        var messages = new List<ContextMessage>
        {
            CreateToolResultMessage("call_0", "tool-0", "payload-0"),
            CreateToolResultMessage("call_1", "tool-1", "payload-1"),
            CreateToolResultMessage("call_2", "tool-2", "payload-2"),
            CreateToolResultMessage("call_3", "tool-3", "payload-3"),
            CreateToolResultMessage("call_4", "tool-4", "payload-4"),
        };

        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(messages[4], 4);
        tokenCounter.Set(messages[3], 4);
        tokenCounter.Set(messages[2], 4);
        tokenCounter.Set(messages[1], 4);
        tokenCounter.Set(messages[0], 4);

        var strategy = new SlidingWindowStrategy(tokenCounter, new SlidingWindowOptions(windowSize: 4, protectedWindowFraction: 0.20));

        // Act
        var compacted = await strategy.CompactAsync(messages, 10);

        // Assert
        Assert.Equal(11, tokenCounter.CountCalls);
        Assert.Equal(CompactionState.Masked, compacted.Messages[0].State);
        Assert.Same(messages[1], compacted.Messages[1]);
        Assert.Same(messages[2], compacted.Messages[2]);
        Assert.Same(messages[3], compacted.Messages[3]);
        Assert.Same(messages[4], compacted.Messages[4]);
        Assert.Equal(20, compacted.TokensBefore);
        Assert.Equal(17, compacted.TokensAfter);
        Assert.Equal(1, compacted.MessagesAffected);
    }

    [Fact]
    public async Task CompactAsync_ProtectedMessages_AreNeverModifiedRegardlessOfContent()
    {
        // Arrange
        var protectedToolResult = new ContextMessage
        {
            Role = MessageRole.User,
            Segments = [new ToolResultContent("call_1", "calculator", "42")],
            State = CompactionState.Summarized,
        };

        var protectedText = ContextMessage.FromText(MessageRole.Model, "keep");
        var exposed = CreateToolResultMessage("call_0", "search", "payload");
        var messages = new List<ContextMessage> { exposed, protectedToolResult, protectedText };

        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(messages[2], 2);
        tokenCounter.Set(messages[1], 2);

        var strategy = new SlidingWindowStrategy(tokenCounter, new SlidingWindowOptions(windowSize: 1, protectedWindowFraction: 0.20));

        // Act
        var compacted = await strategy.CompactAsync(messages, 100);

        // Assert
        Assert.Same(protectedToolResult, compacted.Messages[1]);
        Assert.Same(protectedText, compacted.Messages[2]);
        Assert.Equal(CompactionState.Summarized, compacted.Messages[1].State);
        Assert.Equal(5, compacted.TokensBefore);
        Assert.Equal(5, compacted.TokensAfter);
        Assert.Equal(0, compacted.MessagesAffected);
    }

    [Fact]
    public async Task CompactAsync_WhenOlderToolResultIsOutsideBoundary_MasksExposedMessage()
    {
        // Arrange
        var oldestToolResult = CreateToolResultMessage("call_0", "tool-0", "payload-0");
        var olderToolResult = CreateToolResultMessage("call_1", "tool-1", "payload-1");
        var newest = ContextMessage.FromText(MessageRole.User, "recent");
        var messages = new List<ContextMessage> { oldestToolResult, olderToolResult, newest };

        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(newest, 4);
        tokenCounter.Set(olderToolResult, 4);
        tokenCounter.Set(oldestToolResult, 4);

        var strategy = new SlidingWindowStrategy(tokenCounter, new SlidingWindowOptions(windowSize: 1, protectedWindowFraction: 0.40));

        // Act
        var compacted = await strategy.CompactAsync(messages, 10);

        // Assert
        Assert.Equal(CompactionState.Masked, compacted.Messages[0].State);
        Assert.Equal(CompactionState.Masked, compacted.Messages[1].State);
    }

    [Fact]
    public async Task CompactAsync_WhenBoundaryProtectsNewestWindow_OnlyPreBoundaryMessagesAreAffected()
    {
        // Arrange
        var olderToolResult = CreateToolResultMessage("call_0", "tool-0", "payload-0");
        var protectedOlder = ContextMessage.FromText(MessageRole.User, "older-keep");
        var newest = ContextMessage.FromText(MessageRole.Model, "newest-keep");
        var messages = new List<ContextMessage> { olderToolResult, protectedOlder, newest };

        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(newest, 4);
        tokenCounter.Set(protectedOlder, 4);
        tokenCounter.Set(olderToolResult, 4);

        var strategy = new SlidingWindowStrategy(tokenCounter, new SlidingWindowOptions(windowSize: 2, protectedWindowFraction: 0.80));

        // Act
        var compacted = await strategy.CompactAsync(messages, 10);

        // Assert
        Assert.Equal(CompactionState.Masked, compacted.Messages[0].State);
        Assert.Same(protectedOlder, compacted.Messages[1]);
        Assert.Same(newest, compacted.Messages[2]);
        Assert.Equal(1, compacted.MessagesAffected);
    }

    [Fact]
    public async Task CompactAsync_ToolResultBlocksInExposedSegment_AreReplacedWithTextPlaceholders()
    {
        // Arrange
        var messages = new List<ContextMessage>
        {
            CreateToolResultMessage("call_1", "calculator", "sensitive-output"),
            ContextMessage.FromText(MessageRole.User, "recent"),
        };

        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(messages[1], 4);
        tokenCounter.Set(messages[0], 4);

        var strategy = new SlidingWindowStrategy(tokenCounter, new SlidingWindowOptions(windowSize: 1, protectedWindowFraction: 0.20));

        // Act
        var compacted = await strategy.CompactAsync(messages, 10);

        // Assert
        var masked = compacted.Messages[0];
        var toolResult = Assert.IsType<ToolResultContent>(Assert.Single(masked.Segments));
        Assert.Equal("call_1", toolResult.ToolCallId);
        Assert.Equal("calculator", toolResult.ToolName);
        Assert.Equal("[Tool result removed to save context — calculator, call_1. Call the tool again if you need this content.]", toolResult.Content);
        Assert.Equal(8, compacted.TokensBefore);
        Assert.Equal(5, compacted.TokensAfter);
        Assert.Equal(1, compacted.MessagesAffected);
    }

    [Fact]
    public async Task CompactAsync_NonToolResultMessagesInExposedSegment_ArePassedThroughUnchanged()
    {
        // Arrange
        var passthrough = ContextMessage.FromText(MessageRole.User, "plain text");
        var protectedMessage = ContextMessage.FromText(MessageRole.Model, "recent");
        var messages = new List<ContextMessage> { passthrough, protectedMessage };

        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(protectedMessage, 6);
        tokenCounter.Set(passthrough, 6);

        var strategy = new SlidingWindowStrategy(tokenCounter, new SlidingWindowOptions(windowSize: 1, protectedWindowFraction: 0.20));

        // Act
        var compacted = await strategy.CompactAsync(messages, 10);

        // Assert
        Assert.Same(passthrough, compacted.Messages[0]);
        Assert.Equal(12, compacted.TokensBefore);
        Assert.Equal(12, compacted.TokensAfter);
        Assert.Equal(0, compacted.MessagesAffected);
    }

    [Fact]
    public async Task CompactAsync_MixedContentMessagesInExposedSegment_OnlyReplaceToolResultBlocks()
    {
        // Arrange
        var mixed = new ContextMessage
        {
            Role = MessageRole.User,
            Segments =
            [
                new TextContent("prefix"),
                new ToolResultContent("call_1", "tool", "payload"),
                new TextContent("suffix"),
            ],
        };

        var protectedMessage = ContextMessage.FromText(MessageRole.Model, "recent");
        var messages = new List<ContextMessage> { mixed, protectedMessage };

        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(protectedMessage, 6);
        tokenCounter.Set(mixed, 6);

        var strategy = new SlidingWindowStrategy(tokenCounter, new SlidingWindowOptions(windowSize: 1, protectedWindowFraction: 0.20));

        // Act
        var compacted = await strategy.CompactAsync(messages, 10);
        var compactedBlocks = compacted.Messages[0].Segments;

        // Assert
        Assert.Equal(3, compactedBlocks.Count);
        Assert.Equal("prefix", Assert.IsType<TextContent>(compactedBlocks[0]).Content);
        var maskedToolResult = Assert.IsType<ToolResultContent>(compactedBlocks[1]);
        Assert.Equal("call_1", maskedToolResult.ToolCallId);
        Assert.Equal("tool", maskedToolResult.ToolName);
        Assert.Equal("[Tool result removed to save context — tool, call_1. Call the tool again if you need this content.]", maskedToolResult.Content);
        Assert.Equal("suffix", Assert.IsType<TextContent>(compactedBlocks[2]).Content);
        Assert.Equal(12, compacted.TokensBefore);
        Assert.Equal(7, compacted.TokensAfter);
        Assert.Equal(1, compacted.MessagesAffected);
    }

    [Fact]
    public async Task CompactAsync_MaskedMessagesAreMarkedMasked_ProtectedMessagesRetainOriginalState()
    {
        // Arrange
        var exposed = CreateToolResultMessage("call_1", "tool", "payload");
        var protectedMessage = ContextMessage.FromText(MessageRole.User, "recent") with { State = CompactionState.Summarized };
        var messages = new List<ContextMessage> { exposed, protectedMessage };

        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(protectedMessage, 6);
        tokenCounter.Set(exposed, 6);

        var strategy = new SlidingWindowStrategy(tokenCounter, new SlidingWindowOptions(windowSize: 1, protectedWindowFraction: 0.20));

        // Act
        var compacted = await strategy.CompactAsync(messages, 10);

        // Assert
        Assert.Equal(CompactionState.Masked, compacted.Messages[0].State);
        Assert.Equal(CompactionState.Summarized, compacted.Messages[1].State);
        Assert.Equal(12, compacted.TokensBefore);
        Assert.Equal(7, compacted.TokensAfter);
        Assert.Equal(1, compacted.MessagesAffected);
    }

    [Fact]
    public async Task CompactAsync_WhenTwoToolCallsShareAnId_EachMaskedResultKeepsItsOwnToolName()
    {
        // Arrange
        var firstToolUse = new ContextMessage { Role = MessageRole.Model, Segments = [new ToolUseContent("call_1", "read_file", "{}")] };
        var firstToolResult = CreateToolResultMessage("call_1", "read_file", "file text");
        var secondToolUse = new ContextMessage { Role = MessageRole.Model, Segments = [new ToolUseContent("call_1", "grep", "{}")] };
        var secondToolResult = CreateToolResultMessage("call_1", "grep", "matches");
        var protectedMessage = ContextMessage.FromText(MessageRole.User, "recent");
        var messages = new List<ContextMessage> { firstToolUse, firstToolResult, secondToolUse, secondToolResult, protectedMessage };

        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(protectedMessage, 7);
        tokenCounter.Set(firstToolResult, 5);
        tokenCounter.Set(secondToolResult, 5);

        var strategy = new SlidingWindowStrategy(tokenCounter, new SlidingWindowOptions(windowSize: 1, protectedWindowFraction: 0.20));

        // Act
        var compacted = await strategy.CompactAsync(messages, 10);

        // Assert
        var firstMasked = Assert.IsType<ToolResultContent>(Assert.Single(compacted.Messages[1].Segments));
        Assert.Equal("read_file", firstMasked.ToolName);
        Assert.Equal("[Tool result removed to save context — read_file, call_1. Call the tool again if you need this content.]", firstMasked.Content);
        var secondMasked = Assert.IsType<ToolResultContent>(Assert.Single(compacted.Messages[3].Segments));
        Assert.Equal("grep", secondMasked.ToolName);
        Assert.Equal("[Tool result removed to save context — grep, call_1. Call the tool again if you need this content.]", secondMasked.Content);
    }

    [Fact]
    public async Task CompactAsync_PlaceholderUsesToolNameOfResult_WhenNoToolUseExists()
    {
        // Arrange
        var exposed = CreateToolResultMessage("call_missing", "result-tool", "payload");
        var protectedMessage = ContextMessage.FromText(MessageRole.Model, "recent");
        var messages = new List<ContextMessage> { exposed, protectedMessage };

        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(protectedMessage, 6);
        tokenCounter.Set(exposed, 6);

        var strategy = new SlidingWindowStrategy(tokenCounter, new SlidingWindowOptions(windowSize: 1, protectedWindowFraction: 0.20));

        // Act
        var compacted = await strategy.CompactAsync(messages, 10);

        // Assert
        var toolResult = Assert.IsType<ToolResultContent>(Assert.Single(compacted.Messages[0].Segments));
        Assert.Equal("call_missing", toolResult.ToolCallId);
        Assert.Equal("result-tool", toolResult.ToolName);
        Assert.Equal("[Tool result removed to save context — result-tool, call_missing. Call the tool again if you need this content.]", toolResult.Content);
        Assert.Equal(12, compacted.TokensBefore);
        Assert.Equal(7, compacted.TokensAfter);
        Assert.Equal(1, compacted.MessagesAffected);
    }

    [Fact]
    public async Task CompactAsync_DoesNotMutateInputListOrInputMessages()
    {
        // Arrange
        var originalMessage = new ContextMessage
        {
            Role = MessageRole.User,
            Segments = [new ToolResultContent("call_1", "tool", "payload")],
            State = CompactionState.Original,
        };

        var protectedMessage = ContextMessage.FromText(MessageRole.Model, "recent");
        var messages = new List<ContextMessage> { originalMessage, protectedMessage };

        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(protectedMessage, 6);
        tokenCounter.Set(originalMessage, 6);

        var strategy = new SlidingWindowStrategy(tokenCounter, new SlidingWindowOptions(windowSize: 1, protectedWindowFraction: 0.20));

        // Act
        var compacted = await strategy.CompactAsync(messages, 10);

        // Assert
        Assert.Equal(2, messages.Count);
        Assert.Same(originalMessage, messages[0]);
        Assert.Equal(CompactionState.Original, originalMessage.State);
        Assert.IsType<ToolResultContent>(originalMessage.Segments[0]);
        Assert.NotSame(originalMessage, compacted.Messages[0]);
        Assert.Equal(12, compacted.TokensBefore);
        Assert.Equal(7, compacted.TokensAfter);
        Assert.Equal(1, compacted.MessagesAffected);
    }

    [Fact]
    public async Task CompactAsync_WithCustomPlaceholderFormat_MasksWithThatFormat()
    {
        // Arrange
        var messages = new List<ContextMessage>
        {
            CreateToolResultMessage("call_1", "read_file", "file text") with { TokenCount = 6 },
            ContextMessage.FromText(MessageRole.User, "recent") with { TokenCount = 6 },
        };

        var options = new SlidingWindowOptions(windowSize: 1, protectedWindowFraction: 0.20, placeholderFormat: "[cleared: {0} / {1}]");
        var strategy = new SlidingWindowStrategy(new TrackingTokenCounter(), options);

        // Act
        var compacted = await strategy.CompactAsync(messages, 10);

        // Assert
        var toolResult = Assert.IsType<ToolResultContent>(Assert.Single(compacted.Messages[0].Segments));
        Assert.Equal("[cleared: read_file / call_1]", toolResult.Content);
    }

    [Fact]
    public async Task CompactAsync_WhenHistoryGrowsByOneToolExchangeAtATime_MovesBoundaryAtMostOncePerStep()
    {
        // Arrange
        var strategy = new SlidingWindowStrategy(new TrackingTokenCounter(), new SlidingWindowOptions(windowSize: 2));
        var history = new List<ContextMessage>();
        var maskedCounts = new List<int>();

        // Act
        for (var exchange = 0; exchange < GrowthExchanges; exchange++)
        {
            AddToolExchange(history, exchange);
            var compacted = await strategy.CompactAsync(history.ToList(), GrowthAvailableTokens);
            maskedCounts.Add(CountMasked(compacted.Messages));
        }

        // Assert
        var moves = Enumerable.Range(1, maskedCounts.Count - 1).Count(i => maskedCounts[i] != maskedCounts[i - 1]);
        var tokensGrownWhileMasking = (GrowthExchanges * ExchangeTokens) - GrowthHighMark;
        Assert.True(maskedCounts[^1] > 0);
        Assert.InRange(moves, 2, (tokensGrownWhileMasking / GrowthStepTokens) + 1);
    }

    [Fact]
    public async Task CompactAsync_WhenBoundaryHoldsBetweenTwoCalls_ReturnsEarlierResultAsPrefixOfLaterResult()
    {
        // Arrange
        var strategy = new SlidingWindowStrategy(new TrackingTokenCounter(), new SlidingWindowOptions(windowSize: 2));
        var history = new List<ContextMessage>();
        var views = new List<IReadOnlyList<string>>();
        var maskedCounts = new List<int>();

        // Act
        for (var exchange = 0; exchange < GrowthExchanges; exchange++)
        {
            AddToolExchange(history, exchange);
            var compacted = await strategy.CompactAsync(history.ToList(), GrowthAvailableTokens);
            views.Add(Render(compacted.Messages));
            maskedCounts.Add(CountMasked(compacted.Messages));
        }

        // Assert
        var heldAfterMaskingStarted = 0;

        for (var i = 1; i < views.Count; i++)
        {
            if (maskedCounts[i] != maskedCounts[i - 1])
            {
                continue;
            }

            Assert.Equal(views[i - 1], views[i].Take(views[i - 1].Count));
            heldAfterMaskingStarted += maskedCounts[i] > 0 ? 1 : 0;
        }

        Assert.True(heldAfterMaskingStarted > 0);
    }

    [Fact]
    public async Task CompactAsync_ForSameMessages_ReturnsSameResultWhetherCalledAfterEachAdditionOrOnce()
    {
        // Arrange
        var options = new SlidingWindowOptions(windowSize: 2);
        var drivenStrategy = new SlidingWindowStrategy(new TrackingTokenCounter(), options);
        var history = new List<ContextMessage>();
        var drivenView = Render([]);

        for (var exchange = 0; exchange < GrowthExchanges; exchange++)
        {
            AddToolExchange(history, exchange);
            drivenView = Render((await drivenStrategy.CompactAsync(history.ToList(), GrowthAvailableTokens)).Messages);
        }

        var freshStrategy = new SlidingWindowStrategy(new TrackingTokenCounter(), options);

        // Act
        var compactedOnce = await freshStrategy.CompactAsync(history, GrowthAvailableTokens);

        // Assert
        Assert.Equal(drivenView, Render(compactedOnce.Messages));
    }

    [Fact]
    public async Task CompactAsync_WhenHistoryGrowsByOneToolExchangeAtATime_NeverReturnsMoreTokensThanMaskingUpToHighMark()
    {
        // Arrange
        var strategy = new SlidingWindowStrategy(new TrackingTokenCounter(), new SlidingWindowOptions(windowSize: 2));
        var history = new List<ContextMessage>();
        var tokensAfter = new List<int>();
        var tokensAfterMaskingUpToHighMark = new List<int>();

        // Act
        for (var exchange = 0; exchange < GrowthExchanges; exchange++)
        {
            AddToolExchange(history, exchange);
            tokensAfter.Add((await strategy.CompactAsync(history.ToList(), GrowthAvailableTokens)).TokensAfter);
            tokensAfterMaskingUpToHighMark.Add(CountTokensWhenMaskedBefore(history, FindProtectedStart(history, 2, GrowthHighMark)));
        }

        // Assert
        Assert.All(tokensAfter.Zip(tokensAfterMaskingUpToHighMark), pair => Assert.True(pair.First <= pair.Second));
    }

    [Theory]
    [InlineData(new[] { 15, 15, 15, 15, 15, 15, 15, 15, 15, 15 }, 6)]
    [InlineData(new[] { 10, 10, 10, 10, 10, 10, 50, 10, 10 }, 7)]
    [InlineData(new[] { 10, 10, 10, 10, 10, 10, 10, 50, 10 }, 6)]
    [InlineData(new[] { 10, 10, 10, 50, 10, 10, 10, 10, 10, 10 }, 4)]
    public async Task CompactAsync_WhenHistoryExceedsHighMark_PlacesBoundaryOnLastStepLineBetweenHighAndLowMarkWalks(
        int[] tokenCounts,
        int expectedBoundary)
    {
        // Arrange
        var messages = tokenCounts
            .Select((tokens, i) => CreateToolResultMessage($"call_{i}", "read_file", $"payload-{i}") with { TokenCount = tokens })
            .ToList();

        var strategy = new SlidingWindowStrategy(new TrackingTokenCounter(), new SlidingWindowOptions(windowSize: 1));

        // Act
        var compacted = await strategy.CompactAsync(messages, 100);

        // Assert
        var boundary = CountMasked(compacted.Messages);
        Assert.InRange(boundary, FindProtectedStart(messages, 1, 80), FindProtectedStart(messages, 1, 60));
        Assert.Equal(expectedBoundary, boundary);
        Assert.All(compacted.Messages.Skip(boundary).Zip(messages.Skip(boundary)), pair => Assert.Same(pair.Second, pair.First));
    }

    [Fact]
    public async Task CompactAsync_WithAStepBudget_SnapsTheLowMarkWalkOfTheAvailableBudgetToTheStepLinesOfTheStepBudget()
    {
        // Arrange
        var messages = new[] { 10, 10, 15, 25, 15, 10, 25, 15, 20, 10 }
            .Select((tokens, i) => CreateToolResultMessage($"call_{i}", "read_file", $"payload-{i}") with { TokenCount = tokens })
            .ToList();

        var strategy = new SlidingWindowStrategy(new TrackingTokenCounter(), new SlidingWindowOptions(windowSize: 1));

        // Act
        var compacted = await strategy.CompactAsync(messages, availableTokens: 120, stepBudgetTokens: 100);

        // Assert
        // The marks of 120 are 96 and 72, so the high walk stops at index 4 and the low walk at index 6. The step of 100 is
        // 20 tokens, which puts step lines at indices 2, 4 and 6; a step of 24 from 120 would put them at 3, 4 and 5.
        Assert.Equal(6, CountMasked(compacted.Messages));
        Assert.All(compacted.Messages.Skip(6).Zip(messages.Skip(6)), pair => Assert.Same(pair.Second, pair.First));
    }

    [Fact]
    public async Task CompactAsync_WithoutAStepBudget_DerivesTheStepLinesFromTheAvailableBudget()
    {
        // Arrange
        var messages = new[] { 10, 10, 15, 25, 15, 10, 25, 15, 20, 10 }
            .Select((tokens, i) => CreateToolResultMessage($"call_{i}", "read_file", $"payload-{i}") with { TokenCount = tokens })
            .ToList();

        var strategy = new SlidingWindowStrategy(new TrackingTokenCounter(), new SlidingWindowOptions(windowSize: 1));

        // Act
        var compacted = await strategy.CompactAsync(messages, 120);

        // Assert
        Assert.Equal(5, CountMasked(compacted.Messages));
    }

    [Fact]
    public async Task CompactAsync_WhenNewestWindowAloneExceedsLowMark_MasksUpToHighMarkBoundary()
    {
        // Arrange
        var messages = new List<ContextMessage>
        {
            CreateToolResultMessage("call_0", "read_file", "payload-0") with { TokenCount = 30 },
            CreateToolResultMessage("call_1", "grep", "payload-1") with { TokenCount = 5 },
            ContextMessage.FromText(MessageRole.Model, "older text") with { TokenCount = 50 },
            ContextMessage.FromText(MessageRole.User, "newest text") with { TokenCount = 50 },
        };

        var strategy = new SlidingWindowStrategy(new TrackingTokenCounter(), new SlidingWindowOptions(windowSize: 2));

        // Act
        var compacted = await strategy.CompactAsync(messages, 100);

        // Assert
        Assert.Equal(CompactionState.Masked, compacted.Messages[0].State);
        Assert.Equal(CompactionState.Masked, compacted.Messages[1].State);
        Assert.Same(messages[2], compacted.Messages[2]);
        Assert.Same(messages[3], compacted.Messages[3]);
    }

    [Fact]
    public async Task CompactAsync_WhenHistoryFitsUnderHighMarkButExceedsLowMark_MasksNothing()
    {
        // Arrange
        var messages = Enumerable.Range(0, 4)
            .Select(i => CreateToolResultMessage($"call_{i}", "read_file", $"payload-{i}") with { TokenCount = 20 })
            .ToList();

        var strategy = new SlidingWindowStrategy(new TrackingTokenCounter(), new SlidingWindowOptions(windowSize: 1));

        // Act
        var compacted = await strategy.CompactAsync(messages, 100);

        // Assert
        Assert.Same(messages, compacted.Messages);
        Assert.Equal(0, compacted.MessagesAffected);
    }

    [Fact]
    public async Task CompactAsync_WhenToolResultIsShorterThanItsPlaceholder_ReturnsItUnchangedAndMasksLongerNeighbour()
    {
        // Arrange
        var shortResult = CreateToolResultMessage("call_0", "write_file", "ok");
        var longResult = CreateToolResultMessage("call_1", "read_file", string.Join(' ', Enumerable.Repeat("a line of file text", 40)));
        var newest = ContextMessage.FromText(MessageRole.User, "recent");
        var messages = new List<ContextMessage> { shortResult, longResult, newest };

        var strategy = new SlidingWindowStrategy(new EstimatedTokenCounter(), new SlidingWindowOptions(windowSize: 1, protectedWindowFraction: 0.20));

        // Act
        var compacted = await strategy.CompactAsync(messages, 10);

        // Assert
        Assert.Same(shortResult, compacted.Messages[0]);
        Assert.Equal(CompactionState.Original, compacted.Messages[0].State);
        Assert.Equal(CompactionState.Masked, compacted.Messages[1].State);
        Assert.Equal(
            "[Tool result removed to save context — read_file, call_1. Call the tool again if you need this content.]",
            Assert.Single(compacted.Messages[1].Segments).Content);
        Assert.Same(newest, compacted.Messages[2]);
    }

    [Fact]
    public async Task CompactAsync_WhenMessageHoldsShortAndLongToolResults_ReplacesOnlyTheLongOnes()
    {
        // Arrange
        var shortFirst = new ToolResultContent("call_0", "write_file", "ok");
        var longResult = new ToolResultContent("call_1", "read_file", new string('x', 400));
        var shortLast = new ToolResultContent("call_2", "run", "exit 0");
        var mixed = new ContextMessage { Role = MessageRole.Tool, Segments = [shortFirst, longResult, shortLast] };
        var messages = new List<ContextMessage> { mixed, ContextMessage.FromText(MessageRole.User, "recent") };

        var options = new SlidingWindowOptions(windowSize: 1, protectedWindowFraction: 0.20);
        var strategy = new SlidingWindowStrategy(new TextLengthTokenCounter(), options);

        // Act
        var compacted = await strategy.CompactAsync(messages, 10);

        // Assert
        var segments = compacted.Messages[0].Segments;
        Assert.Equal(CompactionState.Masked, compacted.Messages[0].State);
        Assert.Equal(3, segments.Count);
        Assert.Same(shortFirst, segments[0]);
        Assert.Equal(
            "[Tool result removed to save context — read_file, call_1. Call the tool again if you need this content.]",
            segments[1].Content);
        Assert.Same(shortLast, segments[2]);
        Assert.Equal(1, compacted.MessagesAffected);
    }

    [Fact]
    public async Task CompactAsync_WhenEveryToolResultInMessageIsShorterThanItsPlaceholder_ReturnsSameMessageWithItsTokenCount()
    {
        // Arrange
        var allShort = new ContextMessage
        {
            Role = MessageRole.Tool,
            Segments = [new ToolResultContent("call_0", "write_file", "ok"), new ToolResultContent("call_1", "run", "exit 0")],
            TokenCount = 8,
        };
        var messages = new List<ContextMessage> { allShort, ContextMessage.FromText(MessageRole.User, "recent") };

        var options = new SlidingWindowOptions(windowSize: 1, protectedWindowFraction: 0.20);
        var strategy = new SlidingWindowStrategy(new TextLengthTokenCounter(), options);

        // Act
        var compacted = await strategy.CompactAsync(messages, 10);

        // Assert
        Assert.Same(allShort, compacted.Messages[0]);
        Assert.Equal(CompactionState.Original, compacted.Messages[0].State);
        Assert.Equal(8, compacted.Messages[0].TokenCount);
        Assert.Equal(0, compacted.MessagesAffected);
        Assert.Equal(compacted.TokensBefore, compacted.TokensAfter);
    }

    [Fact]
    public async Task CompactAsync_WhenToolResultIsLongerThanCustomPlaceholderButShorterThanDefaultOne_MasksIt()
    {
        // Arrange
        var messages = new List<ContextMessage>
        {
            CreateToolResultMessage("call_1", "run", "Build succeeded with 0 warnings and 0 errors."),
            ContextMessage.FromText(MessageRole.User, "recent"),
        };

        var options = new SlidingWindowOptions(windowSize: 1, protectedWindowFraction: 0.20, placeholderFormat: "[cleared: {0} / {1}]");
        var strategy = new SlidingWindowStrategy(new TextLengthTokenCounter(), options);

        // Act
        var compacted = await strategy.CompactAsync(messages, 10);

        // Assert
        Assert.Equal(CompactionState.Masked, compacted.Messages[0].State);
        Assert.Equal("[cleared: run / call_1]", Assert.Single(compacted.Messages[0].Segments).Content);
    }

    [Fact]
    public async Task CompactAsync_WhenToolResultIsShorterThanCustomPlaceholderButLongerThanDefaultOne_ReturnsItUnchanged()
    {
        // Arrange
        var result = CreateToolResultMessage("call_1", "read_file", new string('x', 150));
        var messages = new List<ContextMessage> { result, ContextMessage.FromText(MessageRole.User, "recent") };

        var placeholderFormat = "[{0} / {1}: " + new string('-', 200) + "]";
        var options = new SlidingWindowOptions(windowSize: 1, protectedWindowFraction: 0.20, placeholderFormat: placeholderFormat);
        var strategy = new SlidingWindowStrategy(new TextLengthTokenCounter(), options);

        // Act
        var compacted = await strategy.CompactAsync(messages, 10);

        // Assert
        Assert.Same(result, compacted.Messages[0]);
        Assert.Equal(0, compacted.MessagesAffected);
    }

    [Fact]
    public async Task CompactAsync_WhenToolResultCostsTheSameAsItsPlaceholder_ReturnsItUnchanged()
    {
        // Arrange
        var result = CreateToolResultMessage("call_1", "run", new string('x', "[cleared: run / call_1]".Length));
        var messages = new List<ContextMessage> { result, ContextMessage.FromText(MessageRole.User, "recent") };

        var options = new SlidingWindowOptions(windowSize: 1, protectedWindowFraction: 0.20, placeholderFormat: "[cleared: {0} / {1}]");
        var strategy = new SlidingWindowStrategy(new TextLengthTokenCounter(), options);

        // Act
        var compacted = await strategy.CompactAsync(messages, 10);

        // Assert
        Assert.Same(result, compacted.Messages[0]);
    }

    [Fact]
    public async Task CompactAsync_WhenShortToolResultsStayUnmaskedBeforeBoundary_KeepsBoundaryOnItsStepLine()
    {
        // Arrange
        // The counter returns the content length, so the history costs 200, 2, 200, 2 and then six times 200 tokens.
        // With 1,000 available tokens the high mark is 800, the step is 200 and the low mark is 600. The walk with the
        // low mark stops at index 7, and the step lines fall at 1, 3, 5, 6 and 7, so the boundary is index 7.
        var messages = new[] { 200, 2, 200, 2, 200, 200, 200, 200, 200, 200 }
            .Select((length, i) => CreateToolResultMessage($"call_{i}", "read_file", new string('x', length)))
            .ToList();

        var strategy = new SlidingWindowStrategy(new TextLengthTokenCounter(), new SlidingWindowOptions(windowSize: 1));

        // Act
        var compacted = await strategy.CompactAsync(messages, 1_000);

        // Assert
        var states = compacted.Messages.Select(message => message.State).ToArray();
        Assert.Equal(
            [
                CompactionState.Masked, CompactionState.Original, CompactionState.Masked, CompactionState.Original, CompactionState.Masked,
                CompactionState.Masked, CompactionState.Masked, CompactionState.Original, CompactionState.Original, CompactionState.Original,
            ],
            states);
        Assert.Same(messages[1], compacted.Messages[1]);
        Assert.Same(messages[3], compacted.Messages[3]);
        Assert.All(compacted.Messages.Skip(7).Zip(messages.Skip(7)), pair => Assert.Same(pair.Second, pair.First));
    }

    [Fact]
    public async Task CompactAsync_WhenSomeToolResultsStayUnmasked_CountsOnlyMessagesWithReplacedResult()
    {
        // Arrange
        var messages = new List<ContextMessage>
        {
            CreateToolResultMessage("call_0", "write_file", "ok"),
            CreateToolResultMessage("call_1", "read_file", new string('x', 400)),
            CreateToolResultMessage("call_2", "run", "exit 0"),
            CreateToolResultMessage("call_3", "read_file", new string('y', 400)),
            ContextMessage.FromText(MessageRole.User, "recent"),
        };

        var options = new SlidingWindowOptions(windowSize: 1, protectedWindowFraction: 0.20);
        var strategy = new SlidingWindowStrategy(new TextLengthTokenCounter(), options);

        // Act
        var compacted = await strategy.CompactAsync(messages, 10);

        // Assert
        Assert.Equal(2, compacted.MessagesAffected);
        Assert.Equal(2, CountMasked(compacted.Messages));
    }

    [Fact]
    public void SlidingWindowOptions_Default_ReturnsExpectedValues()
    {
        // Arrange

        // Act
        var options = SlidingWindowOptions.Default;

        // Assert
        Assert.Equal(10, options.WindowSize);
        Assert.Equal(0.80, options.ProtectedWindowFraction);
        Assert.Equal("[Tool result removed to save context — {0}, {1}. Call the tool again if you need this content.]", options.PlaceholderFormat);
    }

    [Fact]
    public void SlidingWindowOptions_ParameterlessConstructor_ReturnsDocumentedDefaults()
    {
        // Arrange

        // Act
        var options = new SlidingWindowOptions();

        // Assert
        Assert.Equal(10, options.WindowSize);
        Assert.Equal(0.80, options.ProtectedWindowFraction);
        Assert.Equal("[Tool result removed to save context — {0}, {1}. Call the tool again if you need this content.]", options.PlaceholderFormat);
    }

    [Theory]
    [InlineData("{0} {1} {2}")]
    [InlineData("{0} {")]
    [InlineData("removed }")]
    public void SlidingWindowOptions_ThrowsForAPlaceholderFormatThatCannotBeFormatted(string placeholderFormat)
    {
        // Arrange

        // Act
        Action act = () => _ = new SlidingWindowOptions(placeholderFormat: placeholderFormat);

        // Assert
        var exception = Assert.Throws<ArgumentException>(act);
        Assert.Equal("placeholderFormat", exception.ParamName);
    }

    [Theory]
    [InlineData("removed")]
    [InlineData("{1}")]
    [InlineData("{{{0}}} {1,10}")]
    public void SlidingWindowOptions_AcceptsAPlaceholderFormatThatUsesOnlyTheToolNameAndCallId(string placeholderFormat)
    {
        // Arrange

        // Act
        var options = new SlidingWindowOptions(placeholderFormat: placeholderFormat);

        // Assert
        Assert.Equal(placeholderFormat, options.PlaceholderFormat);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SlidingWindowOptions_ThrowsForInvalidWindowSize(int windowSize)
    {
        // Arrange

        // Act
        Action act = () => _ = new SlidingWindowOptions(windowSize, 0.40, "{0} {1}");

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(act);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.01)]
    [InlineData(1.0)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void SlidingWindowOptions_ThrowsForInvalidProtectedWindowFraction(double protectedWindowFraction)
    {
        // Arrange

        // Act
        Action act = () => _ = new SlidingWindowOptions(1, protectedWindowFraction, "{0} {1}");

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(act);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SlidingWindowOptions_ThrowsForInvalidPlaceholder(string? placeholder)
    {
        // Arrange

        // Act
        Action act = () => _ = new SlidingWindowOptions(1, 0.40, placeholder!);

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    /// <summary>
    /// Creates a semantic message containing a single tool result segment.
    /// </summary>
    /// <param name="toolCallId">The tool call identifier associated with the result.</param>
    /// <param name="toolName">The tool name stored on the result segment.</param>
    /// <param name="payload">The payload captured in the tool result.</param>
    /// <returns>A semantic message representing a tool result.</returns>
    private static ContextMessage CreateToolResultMessage(string toolCallId, string toolName, string payload)
    {
        return new ContextMessage
        {
            Role = MessageRole.User,
            Segments = [new ToolResultContent(toolCallId, toolName, payload)],
        };
    }

    /// <summary>
    /// Appends one tool exchange of <see cref="ExchangeTokens"/> tokens: a model tool call and its result.
    /// </summary>
    /// <param name="history">The history to append to.</param>
    /// <param name="exchange">The zero-based number of the exchange, used in the tool call identifier.</param>
    private static void AddToolExchange(List<ContextMessage> history, int exchange)
    {
        var toolCallId = $"call_{exchange}";

        history.Add(new ContextMessage
        {
            Role = MessageRole.Model,
            Segments = [new ToolUseContent(toolCallId, "read_file", "{}")],
            TokenCount = 10,
        });
        history.Add(CreateToolResultMessage(toolCallId, "read_file", $"payload-{exchange}") with { TokenCount = ExchangeTokens - 10 });
    }

    private static int CountMasked(IReadOnlyList<ContextMessage> messages)
    {
        return messages.Count(message => message.State == CompactionState.Masked);
    }

    /// <summary>
    /// Renders each message as text so two results can be compared by content.
    /// </summary>
    /// <param name="messages">The messages to render.</param>
    /// <returns>One line per message holding its role, state, and segment content.</returns>
    private static IReadOnlyList<string> Render(IReadOnlyList<ContextMessage> messages)
    {
        return messages
            .Select(message => $"{message.Role}|{message.State}|{string.Join("|", message.Segments.Select(segment => segment.ToString()))}")
            .ToList();
    }

    /// <summary>
    /// Finds the first message protected by a backward walk that keeps a floor of newest messages and then a token allowance.
    /// </summary>
    /// <param name="messages">The history, oldest first, with <see cref="ContextMessage.TokenCount"/> set on every message.</param>
    /// <param name="windowSize">The number of newest messages that are always protected.</param>
    /// <param name="allowance">The most tokens the protected messages may hold once the floor is met.</param>
    /// <returns>The index of the first protected message.</returns>
    private static int FindProtectedStart(IReadOnlyList<ContextMessage> messages, int windowSize, int allowance)
    {
        var protectedTokens = 0;
        var start = messages.Count;

        while (start > 0 && (messages.Count - start < windowSize || protectedTokens + messages[start - 1].TokenCount!.Value <= allowance))
        {
            start--;
            protectedTokens += messages[start].TokenCount!.Value;
        }

        return start;
    }

    /// <summary>
    /// Totals a history as if every tool result before an index were masked to a one-token placeholder message.
    /// </summary>
    /// <param name="messages">The history, oldest first, with <see cref="ContextMessage.TokenCount"/> set on every message.</param>
    /// <param name="boundary">The index of the first message that keeps its content.</param>
    /// <returns>The token total of the history masked up to <paramref name="boundary"/>.</returns>
    private static int CountTokensWhenMaskedBefore(IReadOnlyList<ContextMessage> messages, int boundary)
    {
        return messages
            .Select((message, i) => i < boundary && message.Segments.Any(segment => segment is ToolResultContent) ? 1 : message.TokenCount!.Value)
            .Sum();
    }

    private sealed class TrackingTokenCounter : ITokenCounter
    {
        private readonly Dictionary<ContextMessage, int> _counts = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<ContextMessage> _counted = new(ReferenceEqualityComparer.Instance);

        /// <summary>
        /// Gets the number of count requests made through this test double.
        /// </summary>
        public int CountCalls { get; private set; }

        /// <summary>
        /// Registers a token count for a specific message instance.
        /// </summary>
        /// <param name="contextMessage">The message whose token count should be returned.</param>
        /// <param name="count">The token count to return.</param>
        public void Set(ContextMessage contextMessage, int count)
        {
            this._counts[contextMessage] = count;
        }

        /// <summary>
        /// Determines whether the specified message has been counted.
        /// </summary>
        /// <param name="contextMessage">The message to check.</param>
        /// <returns><see langword="true"/> when the message has been counted; otherwise, <see langword="false"/>.</returns>
        public bool WasCounted(ContextMessage contextMessage)
        {
            return this._counted.Contains(contextMessage);
        }

        /// <summary>
        /// Returns the configured token count for a single message and records the invocation.
        /// </summary>
        /// <param name="contextMessage">The message to count.</param>
        /// <returns>The configured token count for the message.</returns>
        public int Count(ContextMessage contextMessage)
        {
            this.CountCalls++;
            this._counted.Add(contextMessage);

            return this._counts.TryGetValue(contextMessage, out var value)
                ? value
                : 1;
        }

        /// <summary>
        /// Returns the total configured token count for a sequence of messages.
        /// </summary>
        /// <param name="messages">The messages to count.</param>
        /// <returns>The total token count across the sequence.</returns>
        public int Count(IEnumerable<ContextMessage> messages)
        {
            ArgumentNullException.ThrowIfNull(messages);

            var total = 0;
            foreach (var message in messages)
            {
                total += this.Count(message);
            }

            return total;
        }
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<ContextMessage>
    {
        /// <summary>
        /// Gets the shared comparer instance.
        /// </summary>
        public static readonly ReferenceEqualityComparer Instance = new();

        /// <summary>
        /// Determines whether two message references are the same instance.
        /// </summary>
        /// <param name="x">The first message reference.</param>
        /// <param name="y">The second message reference.</param>
        /// <returns><see langword="true"/> when both references point to the same instance; otherwise, <see langword="false"/>.</returns>
        public bool Equals(ContextMessage? x, ContextMessage? y)
        {
            return ReferenceEquals(x, y);
        }

        /// <summary>
        /// Returns a hash code based on object identity.
        /// </summary>
        /// <param name="obj">The message reference to hash.</param>
        /// <returns>A hash code derived from the object identity.</returns>
        public int GetHashCode(ContextMessage obj)
        {
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
