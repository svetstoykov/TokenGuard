using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;
using TokenGuard.Core.Options;
using TokenGuard.Core.Strategies;

namespace TokenGuard.Tests.Strategies;

public sealed class SlidingWindowStrategyTests
{
    [Fact]
    public async Task CompactAsync_WhenAllMessagesFitWithinWindowAndTokenCap_ReturnsOriginalListReference()
    {
        var messages = new List<SemanticMessage>
        {
            SemanticMessage.FromText(MessageRole.User, "one"),
            SemanticMessage.FromText(MessageRole.Model, "two"),
        };

        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(messages[1], 2);
        tokenCounter.Set(messages[0], 2);

        var strategy = new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 10, protectedWindowFraction: 0.90));

        var compacted = await strategy.CompactAsync(messages, ContextBudget.For(100), tokenCounter);

        Assert.Same(messages, compacted);
    }

    [Fact]
    public async Task CompactAsync_WhenTokenCapFiresBeforeWindowSize_BoundaryIsCorrectAndWalkStopsEarly()
    {
        var messages = new List<SemanticMessage>
        {
            SemanticMessage.FromText(MessageRole.User, "m0"),
            SemanticMessage.FromText(MessageRole.User, "m1"),
            SemanticMessage.FromText(MessageRole.User, "m2"),
            CreateToolResultMessage("call_3", "tool-3", "payload-3"),
            CreateToolResultMessage("call_4", "tool-4", "payload-4"),
        };

        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(messages[4], 4);
        tokenCounter.Set(messages[3], 4);

        var strategy = new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 4, protectedWindowFraction: 0.50));

        var compacted = await strategy.CompactAsync(messages, ContextBudget.For(10), tokenCounter);

        Assert.Equal(2, tokenCounter.CountCalls);
        Assert.False(tokenCounter.WasCounted(messages[2]));
        Assert.Same(messages[4], compacted[4]);
        Assert.Equal(CompactionState.Masked, compacted[3].State);
    }

    [Fact]
    public async Task CompactAsync_WhenCountFloorFiresBeforeTokenCap_ProtectsExactlyWindowSizeMessages()
    {
        var messages = new List<SemanticMessage>
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

        var strategy = new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 2, protectedWindowFraction: 0.90));

        var compacted = await strategy.CompactAsync(messages, ContextBudget.For(100), tokenCounter);

        Assert.Equal(2, tokenCounter.CountCalls);
        Assert.Same(messages[3], compacted[3]);
        Assert.Same(messages[4], compacted[4]);
        Assert.Equal(CompactionState.Masked, compacted[2].State);
    }

    [Fact]
    public async Task CompactAsync_ProtectedMessages_AreNeverModifiedRegardlessOfContent()
    {
        var protectedToolResult = new SemanticMessage
        {
            Role = MessageRole.User,
            Content = [new ToolResultContent("call_1", "calculator", "42")],
            State = CompactionState.Summarized,
        };

        var protectedText = SemanticMessage.FromText(MessageRole.Model, "keep");
        var exposed = CreateToolResultMessage("call_0", "search", "payload");
        var messages = new List<SemanticMessage> { exposed, protectedToolResult, protectedText };

        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(messages[2], 2);
        tokenCounter.Set(messages[1], 2);

        var strategy = new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 2, protectedWindowFraction: 0.80));

        var compacted = await strategy.CompactAsync(messages, ContextBudget.For(100), tokenCounter);

        Assert.Same(protectedToolResult, compacted[1]);
        Assert.Same(protectedText, compacted[2]);
        Assert.Equal(CompactionState.Summarized, compacted[1].State);
    }

    [Fact]
    public async Task CompactAsync_ToolResultBlocksInExposedSegment_AreReplacedWithTextPlaceholders()
    {
        var messages = new List<SemanticMessage>
        {
            CreateToolResultMessage("call_1", "calculator", "sensitive-output"),
            SemanticMessage.FromText(MessageRole.User, "recent"),
        };

        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(messages[1], 4);
        tokenCounter.Set(messages[0], 4);

        var strategy = new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 2, protectedWindowFraction: 0.30));

        var compacted = await strategy.CompactAsync(messages, ContextBudget.For(10), tokenCounter);

        var masked = compacted[0];
        var text = Assert.IsType<TextContent>(Assert.Single(masked.Content));
        Assert.Equal("[Tool result cleared — call_1, call_1]", text.Text);
    }

    [Fact]
    public async Task CompactAsync_NonToolResultMessagesInExposedSegment_ArePassedThroughUnchanged()
    {
        var passthrough = SemanticMessage.FromText(MessageRole.User, "plain text");
        var protectedMessage = SemanticMessage.FromText(MessageRole.Model, "recent");
        var messages = new List<SemanticMessage> { passthrough, protectedMessage };

        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(protectedMessage, 6);
        tokenCounter.Set(passthrough, 6);

        var strategy = new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 2, protectedWindowFraction: 0.50));

        var compacted = await strategy.CompactAsync(messages, ContextBudget.For(10), tokenCounter);

        Assert.Same(passthrough, compacted[0]);
    }

    [Fact]
    public async Task CompactAsync_MixedContentMessagesInExposedSegment_OnlyReplaceToolResultBlocks()
    {
        var mixed = new SemanticMessage
        {
            Role = MessageRole.User,
            Content =
            [
                new TextContent("prefix"),
                new ToolResultContent("call_1", "tool", "payload"),
                new TextContent("suffix"),
            ],
        };

        var protectedMessage = SemanticMessage.FromText(MessageRole.Model, "recent");
        var messages = new List<SemanticMessage> { mixed, protectedMessage };

        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(protectedMessage, 6);
        tokenCounter.Set(mixed, 6);

        var strategy = new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 2, protectedWindowFraction: 0.50));

        var compacted = await strategy.CompactAsync(messages, ContextBudget.For(10), tokenCounter);
        var compactedBlocks = compacted[0].Content;

        Assert.Equal(3, compactedBlocks.Count);
        Assert.Equal("prefix", Assert.IsType<TextContent>(compactedBlocks[0]).Text);
        Assert.Equal("[Tool result cleared — call_1, call_1]", Assert.IsType<TextContent>(compactedBlocks[1]).Text);
        Assert.Equal("suffix", Assert.IsType<TextContent>(compactedBlocks[2]).Text);
    }

    [Fact]
    public async Task CompactAsync_MaskedMessagesAreMarkedMasked_ProtectedMessagesRetainOriginalState()
    {
        var exposed = CreateToolResultMessage("call_1", "tool", "payload");
        var protectedMessage = SemanticMessage.FromText(MessageRole.User, "recent") with { State = CompactionState.Summarized };
        var messages = new List<SemanticMessage> { exposed, protectedMessage };

        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(protectedMessage, 6);
        tokenCounter.Set(exposed, 6);

        var strategy = new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 2, protectedWindowFraction: 0.50));

        var compacted = await strategy.CompactAsync(messages, ContextBudget.For(10), tokenCounter);

        Assert.Equal(CompactionState.Masked, compacted[0].State);
        Assert.Equal(CompactionState.Summarized, compacted[1].State);
    }

    [Fact]
    public async Task CompactAsync_PlaceholderUsesResolvedToolName_WhenMatchingToolUseExistsInAnyMessage()
    {
        var toolUseMessage = new SemanticMessage
        {
            Role = MessageRole.Model,
            Content = [new ToolUseContent("call_1", "calculator", "{}")],
        };

        var toolResultMessage = new SemanticMessage
        {
            Role = MessageRole.User,
            Content = [new ToolResultContent("call_1", "ignored-name", "42")],
        };

        var protectedMessage = SemanticMessage.FromText(MessageRole.User, "recent");
        var messages = new List<SemanticMessage> { toolUseMessage, toolResultMessage, protectedMessage };

        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(protectedMessage, 7);
        tokenCounter.Set(toolResultMessage, 7);

        var strategy = new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 3, protectedWindowFraction: 0.50));

        var compacted = await strategy.CompactAsync(messages, ContextBudget.For(10), tokenCounter);

        var text = Assert.IsType<TextContent>(Assert.Single(compacted[1].Content));
        Assert.Equal("[Tool result cleared — calculator, call_1]", text.Text);
    }

    [Fact]
    public async Task CompactAsync_PlaceholderFallsBackToToolCallId_WhenNoToolUseExists()
    {
        var exposed = CreateToolResultMessage("call_missing", "result-tool", "payload");
        var protectedMessage = SemanticMessage.FromText(MessageRole.Model, "recent");
        var messages = new List<SemanticMessage> { exposed, protectedMessage };

        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(protectedMessage, 6);
        tokenCounter.Set(exposed, 6);

        var strategy = new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 2, protectedWindowFraction: 0.50));

        var compacted = await strategy.CompactAsync(messages, ContextBudget.For(10), tokenCounter);

        var text = Assert.IsType<TextContent>(Assert.Single(compacted[0].Content));
        Assert.Equal("[Tool result cleared — call_missing, call_missing]", text.Text);
    }

    [Fact]
    public async Task CompactAsync_DoesNotMutateInputListOrInputMessages()
    {
        var originalMessage = new SemanticMessage
        {
            Role = MessageRole.User,
            Content = [new ToolResultContent("call_1", "tool", "payload")],
            State = CompactionState.Original,
        };

        var protectedMessage = SemanticMessage.FromText(MessageRole.Model, "recent");
        var messages = new List<SemanticMessage> { originalMessage, protectedMessage };

        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(protectedMessage, 6);
        tokenCounter.Set(originalMessage, 6);

        var strategy = new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 2, protectedWindowFraction: 0.50));

        var compacted = await strategy.CompactAsync(messages, ContextBudget.For(10), tokenCounter);

        Assert.Equal(2, messages.Count);
        Assert.Same(originalMessage, messages[0]);
        Assert.Equal(CompactionState.Original, originalMessage.State);
        Assert.IsType<ToolResultContent>(originalMessage.Content[0]);
        Assert.NotSame(originalMessage, compacted[0]);
    }

    [Fact]
    public void SlidingWindowOptions_Default_ReturnsExpectedValues()
    {
        var options = SlidingWindowOptions.Default;

        Assert.Equal(10, options.WindowSize);
        Assert.Equal(0.40, options.ProtectedWindowFraction);
        Assert.Equal("[Tool result cleared — {0}, {1}]", options.PlaceholderFormat);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SlidingWindowOptions_ThrowsForInvalidWindowSize(int windowSize)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new SlidingWindowOptions(windowSize, 0.40, "{0} {1}"));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.01)]
    [InlineData(1.0)]
    [InlineData(1.01)]
    public void SlidingWindowOptions_ThrowsForInvalidProtectedWindowFraction(double protectedWindowFraction)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new SlidingWindowOptions(1, protectedWindowFraction, "{0} {1}"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SlidingWindowOptions_ThrowsForInvalidPlaceholder(string? placeholder)
    {
        Assert.Throws<ArgumentException>(() => _ = new SlidingWindowOptions(1, 0.40, placeholder!));
    }

    private static SemanticMessage CreateToolResultMessage(string toolCallId, string toolName, string payload)
    {
        return new SemanticMessage
        {
            Role = MessageRole.User,
            Content = [new ToolResultContent(toolCallId, toolName, payload)],
        };
    }

    private sealed class TrackingTokenCounter : ITokenCounter
    {
        private readonly Dictionary<SemanticMessage, int> _counts = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<SemanticMessage> _counted = new(ReferenceEqualityComparer.Instance);

        public int CountCalls { get; private set; }

        public void Set(SemanticMessage semanticMessage, int count)
        {
            this._counts[semanticMessage] = count;
        }

        public bool WasCounted(SemanticMessage semanticMessage)
        {
            return this._counted.Contains(semanticMessage);
        }

        public int Count(SemanticMessage semanticMessage)
        {
            this.CountCalls++;
            this._counted.Add(semanticMessage);

            return this._counts.TryGetValue(semanticMessage, out var value)
                ? value
                : 1;
        }

        public int Count(IEnumerable<SemanticMessage> messages)
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

    private sealed class ReferenceEqualityComparer : IEqualityComparer<SemanticMessage>
    {
        public static readonly ReferenceEqualityComparer Instance = new();

        public bool Equals(SemanticMessage? x, SemanticMessage? y)
        {
            return ReferenceEquals(x, y);
        }

        public int GetHashCode(SemanticMessage obj)
        {
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
