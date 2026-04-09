using SemanticFold.Abstractions;
using SemanticFold.Enums;
using SemanticFold.Models;
using SemanticFold.Models.Content;
using SemanticFold.Strategies;

namespace SemanticFold.Tests.Strategies;

public sealed class SlidingWindowStrategyTests
{
    [Fact]
    public void Compact_WhenAllMessagesFitWithinWindowAndTokenCap_ReturnsOriginalListReference()
    {
        var messages = new List<Message>
        {
            Message.FromText(MessageRole.User, "one"),
            Message.FromText(MessageRole.Assistant, "two"),
        };

        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(messages[1], 2);
        tokenCounter.Set(messages[0], 2);

        var strategy = new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 10, protectedWindowFraction: 0.90));

        var compacted = strategy.Compact(messages, ContextBudget.For(100), tokenCounter);

        Assert.Same(messages, compacted);
    }

    [Fact]
    public void Compact_WhenTokenCapFiresBeforeWindowSize_BoundaryIsCorrectAndWalkStopsEarly()
    {
        var messages = new List<Message>
        {
            Message.FromText(MessageRole.User, "m0"),
            Message.FromText(MessageRole.User, "m1"),
            Message.FromText(MessageRole.User, "m2"),
            CreateToolResultMessage("call_3", "tool-3", "payload-3"),
            CreateToolResultMessage("call_4", "tool-4", "payload-4"),
        };

        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(messages[4], 4);
        tokenCounter.Set(messages[3], 4);

        var strategy = new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 4, protectedWindowFraction: 0.50));

        var compacted = strategy.Compact(messages, ContextBudget.For(10), tokenCounter);

        Assert.Equal(2, tokenCounter.CountCalls);
        Assert.False(tokenCounter.WasCounted(messages[2]));
        Assert.Same(messages[4], compacted[4]);
        Assert.Equal(CompactionState.Masked, compacted[3].State);
    }

    [Fact]
    public void Compact_WhenCountFloorFiresBeforeTokenCap_ProtectsExactlyWindowSizeMessages()
    {
        var messages = new List<Message>
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

        var compacted = strategy.Compact(messages, ContextBudget.For(100), tokenCounter);

        Assert.Equal(2, tokenCounter.CountCalls);
        Assert.Same(messages[3], compacted[3]);
        Assert.Same(messages[4], compacted[4]);
        Assert.Equal(CompactionState.Masked, compacted[2].State);
    }

    [Fact]
    public void Compact_ProtectedMessages_AreNeverModifiedRegardlessOfContent()
    {
        var protectedToolResult = new Message
        {
            Role = MessageRole.User,
            Content = [new ToolResultContent("call_1", "calculator", "42")],
            State = CompactionState.Summarized,
        };

        var protectedText = Message.FromText(MessageRole.Assistant, "keep");
        var exposed = CreateToolResultMessage("call_0", "search", "payload");
        var messages = new List<Message> { exposed, protectedToolResult, protectedText };

        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(messages[2], 2);
        tokenCounter.Set(messages[1], 2);

        var strategy = new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 2, protectedWindowFraction: 0.80));

        var compacted = strategy.Compact(messages, ContextBudget.For(100), tokenCounter);

        Assert.Same(protectedToolResult, compacted[1]);
        Assert.Same(protectedText, compacted[2]);
        Assert.Equal(CompactionState.Summarized, compacted[1].State);
    }

    [Fact]
    public void Compact_ToolResultBlocksInExposedSegment_AreReplacedWithTextPlaceholders()
    {
        var messages = new List<Message>
        {
            CreateToolResultMessage("call_1", "calculator", "sensitive-output"),
            Message.FromText(MessageRole.User, "recent"),
        };

        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(messages[1], 4);
        tokenCounter.Set(messages[0], 4);

        var strategy = new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 2, protectedWindowFraction: 0.30));

        var compacted = strategy.Compact(messages, ContextBudget.For(10), tokenCounter);

        var masked = compacted[0];
        var text = Assert.IsType<TextContent>(Assert.Single(masked.Content));
        Assert.Equal("[Tool result cleared — call_1, call_1]", text.Text);
    }

    [Fact]
    public void Compact_NonToolResultMessagesInExposedSegment_ArePassedThroughUnchanged()
    {
        var passthrough = Message.FromText(MessageRole.User, "plain text");
        var protectedMessage = Message.FromText(MessageRole.Assistant, "recent");
        var messages = new List<Message> { passthrough, protectedMessage };

        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(protectedMessage, 6);
        tokenCounter.Set(passthrough, 6);

        var strategy = new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 2, protectedWindowFraction: 0.50));

        var compacted = strategy.Compact(messages, ContextBudget.For(10), tokenCounter);

        Assert.Same(passthrough, compacted[0]);
    }

    [Fact]
    public void Compact_MixedContentMessagesInExposedSegment_OnlyReplaceToolResultBlocks()
    {
        var mixed = new Message
        {
            Role = MessageRole.User,
            Content =
            [
                new TextContent("prefix"),
                new ToolResultContent("call_1", "tool", "payload"),
                new TextContent("suffix"),
            ],
        };

        var protectedMessage = Message.FromText(MessageRole.Assistant, "recent");
        var messages = new List<Message> { mixed, protectedMessage };

        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(protectedMessage, 6);
        tokenCounter.Set(mixed, 6);

        var strategy = new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 2, protectedWindowFraction: 0.50));

        var compacted = strategy.Compact(messages, ContextBudget.For(10), tokenCounter);
        var compactedBlocks = compacted[0].Content;

        Assert.Equal(3, compactedBlocks.Count);
        Assert.Equal("prefix", Assert.IsType<TextContent>(compactedBlocks[0]).Text);
        Assert.Equal("[Tool result cleared — call_1, call_1]", Assert.IsType<TextContent>(compactedBlocks[1]).Text);
        Assert.Equal("suffix", Assert.IsType<TextContent>(compactedBlocks[2]).Text);
    }

    [Fact]
    public void Compact_MaskedMessagesAreMarkedMasked_ProtectedMessagesRetainOriginalState()
    {
        var exposed = CreateToolResultMessage("call_1", "tool", "payload");
        var protectedMessage = Message.FromText(MessageRole.User, "recent") with { State = CompactionState.Summarized };
        var messages = new List<Message> { exposed, protectedMessage };

        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(protectedMessage, 6);
        tokenCounter.Set(exposed, 6);

        var strategy = new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 2, protectedWindowFraction: 0.50));

        var compacted = strategy.Compact(messages, ContextBudget.For(10), tokenCounter);

        Assert.Equal(CompactionState.Masked, compacted[0].State);
        Assert.Equal(CompactionState.Summarized, compacted[1].State);
    }

    [Fact]
    public void Compact_PlaceholderUsesResolvedToolName_WhenMatchingToolUseExistsInAnyMessage()
    {
        var toolUseMessage = new Message
        {
            Role = MessageRole.Assistant,
            Content = [new ToolUseContent("call_1", "calculator", "{}")],
        };

        var toolResultMessage = new Message
        {
            Role = MessageRole.User,
            Content = [new ToolResultContent("call_1", "ignored-name", "42")],
        };

        var protectedMessage = Message.FromText(MessageRole.User, "recent");
        var messages = new List<Message> { toolUseMessage, toolResultMessage, protectedMessage };

        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(protectedMessage, 7);
        tokenCounter.Set(toolResultMessage, 7);

        var strategy = new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 3, protectedWindowFraction: 0.50));

        var compacted = strategy.Compact(messages, ContextBudget.For(10), tokenCounter);

        var text = Assert.IsType<TextContent>(Assert.Single(compacted[1].Content));
        Assert.Equal("[Tool result cleared — calculator, call_1]", text.Text);
    }

    [Fact]
    public void Compact_PlaceholderFallsBackToToolCallId_WhenNoToolUseExists()
    {
        var exposed = CreateToolResultMessage("call_missing", "result-tool", "payload");
        var protectedMessage = Message.FromText(MessageRole.Assistant, "recent");
        var messages = new List<Message> { exposed, protectedMessage };

        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(protectedMessage, 6);
        tokenCounter.Set(exposed, 6);

        var strategy = new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 2, protectedWindowFraction: 0.50));

        var compacted = strategy.Compact(messages, ContextBudget.For(10), tokenCounter);

        var text = Assert.IsType<TextContent>(Assert.Single(compacted[0].Content));
        Assert.Equal("[Tool result cleared — call_missing, call_missing]", text.Text);
    }

    [Fact]
    public void Compact_DoesNotMutateInputListOrInputMessages()
    {
        var originalMessage = new Message
        {
            Role = MessageRole.User,
            Content = [new ToolResultContent("call_1", "tool", "payload")],
            State = CompactionState.Original,
        };

        var protectedMessage = Message.FromText(MessageRole.Assistant, "recent");
        var messages = new List<Message> { originalMessage, protectedMessage };

        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(protectedMessage, 6);
        tokenCounter.Set(originalMessage, 6);

        var strategy = new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 2, protectedWindowFraction: 0.50));

        var compacted = strategy.Compact(messages, ContextBudget.For(10), tokenCounter);

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

    private static Message CreateToolResultMessage(string toolCallId, string toolName, string payload)
    {
        return new Message
        {
            Role = MessageRole.User,
            Content = [new ToolResultContent(toolCallId, toolName, payload)],
        };
    }

    private sealed class TrackingTokenCounter : ITokenCounter
    {
        private readonly Dictionary<Message, int> _counts = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<Message> _counted = new(ReferenceEqualityComparer.Instance);

        public int CountCalls { get; private set; }

        public void Set(Message message, int count)
        {
            this._counts[message] = count;
        }

        public bool WasCounted(Message message)
        {
            return this._counted.Contains(message);
        }

        public int Count(Message message)
        {
            this.CountCalls++;
            this._counted.Add(message);

            return this._counts.TryGetValue(message, out var value)
                ? value
                : 1;
        }

        public int Count(IEnumerable<Message> messages)
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

    private sealed class ReferenceEqualityComparer : IEqualityComparer<Message>
    {
        public static readonly ReferenceEqualityComparer Instance = new();

        public bool Equals(Message? x, Message? y)
        {
            return ReferenceEquals(x, y);
        }

        public int GetHashCode(Message obj)
        {
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
