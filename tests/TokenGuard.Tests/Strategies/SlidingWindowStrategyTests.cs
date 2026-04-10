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
        // Arrange
        var messages = new List<ContextMessage>
        {
            ContextMessage.FromText(MessageRole.User, "one"),
            ContextMessage.FromText(MessageRole.Model, "two"),
        };

        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(messages[1], 2);
        tokenCounter.Set(messages[0], 2);

        var strategy = new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 10, protectedWindowFraction: 0.90));

        // Act
        var compacted = await strategy.CompactAsync(messages, ContextBudget.For(100), tokenCounter);

        // Assert
        Assert.Same(messages, compacted);
    }

    [Fact]
    public async Task CompactAsync_WhenTokenCapFiresBeforeWindowSize_BoundaryIsCorrectAndWalkStopsEarly()
    {
        // Arrange
        var messages = new List<ContextMessage>
        {
            ContextMessage.FromText(MessageRole.User, "m0"),
            ContextMessage.FromText(MessageRole.User, "m1"),
            ContextMessage.FromText(MessageRole.User, "m2"),
            CreateToolResultMessage("call_3", "tool-3", "payload-3"),
            CreateToolResultMessage("call_4", "tool-4", "payload-4"),
        };

        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(messages[4], 4);
        tokenCounter.Set(messages[3], 4);

        var strategy = new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 4, protectedWindowFraction: 0.50));

        // Act
        var compacted = await strategy.CompactAsync(messages, ContextBudget.For(10), tokenCounter);

        // Assert
        Assert.Equal(2, tokenCounter.CountCalls);
        Assert.False(tokenCounter.WasCounted(messages[2]));
        Assert.Same(messages[4], compacted[4]);
        Assert.Equal(CompactionState.Masked, compacted[3].State);
    }

    [Fact]
    public async Task CompactAsync_WhenCountFloorFiresBeforeTokenCap_ProtectsExactlyWindowSizeMessages()
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

        var strategy = new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 2, protectedWindowFraction: 0.90));

        // Act
        var compacted = await strategy.CompactAsync(messages, ContextBudget.For(100), tokenCounter);

        // Assert
        Assert.Equal(2, tokenCounter.CountCalls);
        Assert.Same(messages[3], compacted[3]);
        Assert.Same(messages[4], compacted[4]);
        Assert.Equal(CompactionState.Masked, compacted[2].State);
    }

    [Fact]
    public async Task CompactAsync_ProtectedMessages_AreNeverModifiedRegardlessOfContent()
    {
        // Arrange
        var protectedToolResult = new ContextMessage
        {
            Role = MessageRole.User,
            Content = [new ToolResultContent("call_1", "calculator", "42")],
            State = CompactionState.Summarized,
        };

        var protectedText = ContextMessage.FromText(MessageRole.Model, "keep");
        var exposed = CreateToolResultMessage("call_0", "search", "payload");
        var messages = new List<ContextMessage> { exposed, protectedToolResult, protectedText };

        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(messages[2], 2);
        tokenCounter.Set(messages[1], 2);

        var strategy = new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 2, protectedWindowFraction: 0.80));

        // Act
        var compacted = await strategy.CompactAsync(messages, ContextBudget.For(100), tokenCounter);

        // Assert
        Assert.Same(protectedToolResult, compacted[1]);
        Assert.Same(protectedText, compacted[2]);
        Assert.Equal(CompactionState.Summarized, compacted[1].State);
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

        var strategy = new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 2, protectedWindowFraction: 0.30));

        // Act
        var compacted = await strategy.CompactAsync(messages, ContextBudget.For(10), tokenCounter);

        // Assert
        var masked = compacted[0];
        var text = Assert.IsType<TextContent>(Assert.Single(masked.Content));
        Assert.Equal("[Tool result cleared — call_1, call_1]", text.Text);
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

        var strategy = new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 2, protectedWindowFraction: 0.50));

        // Act
        var compacted = await strategy.CompactAsync(messages, ContextBudget.For(10), tokenCounter);

        // Assert
        Assert.Same(passthrough, compacted[0]);
    }

    [Fact]
    public async Task CompactAsync_MixedContentMessagesInExposedSegment_OnlyReplaceToolResultBlocks()
    {
        // Arrange
        var mixed = new ContextMessage
        {
            Role = MessageRole.User,
            Content =
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

        var strategy = new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 2, protectedWindowFraction: 0.50));

        // Act
        var compacted = await strategy.CompactAsync(messages, ContextBudget.For(10), tokenCounter);
        var compactedBlocks = compacted[0].Content;

        // Assert
        Assert.Equal(3, compactedBlocks.Count);
        Assert.Equal("prefix", Assert.IsType<TextContent>(compactedBlocks[0]).Text);
        Assert.Equal("[Tool result cleared — call_1, call_1]", Assert.IsType<TextContent>(compactedBlocks[1]).Text);
        Assert.Equal("suffix", Assert.IsType<TextContent>(compactedBlocks[2]).Text);
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

        var strategy = new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 2, protectedWindowFraction: 0.50));

        // Act
        var compacted = await strategy.CompactAsync(messages, ContextBudget.For(10), tokenCounter);

        // Assert
        Assert.Equal(CompactionState.Masked, compacted[0].State);
        Assert.Equal(CompactionState.Summarized, compacted[1].State);
    }

    [Fact]
    public async Task CompactAsync_PlaceholderUsesResolvedToolName_WhenMatchingToolUseExistsInAnyMessage()
    {
        // Arrange
        var toolUseMessage = new ContextMessage
        {
            Role = MessageRole.Model,
            Content = [new ToolUseContent("call_1", "calculator", "{}")],
        };

        var toolResultMessage = new ContextMessage
        {
            Role = MessageRole.User,
            Content = [new ToolResultContent("call_1", "ignored-name", "42")],
        };

        var protectedMessage = ContextMessage.FromText(MessageRole.User, "recent");
        var messages = new List<ContextMessage> { toolUseMessage, toolResultMessage, protectedMessage };

        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(protectedMessage, 7);
        tokenCounter.Set(toolResultMessage, 7);

        var strategy = new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 3, protectedWindowFraction: 0.50));

        // Act
        var compacted = await strategy.CompactAsync(messages, ContextBudget.For(10), tokenCounter);

        // Assert
        var text = Assert.IsType<TextContent>(Assert.Single(compacted[1].Content));
        Assert.Equal("[Tool result cleared — calculator, call_1]", text.Text);
    }

    [Fact]
    public async Task CompactAsync_PlaceholderFallsBackToToolCallId_WhenNoToolUseExists()
    {
        // Arrange
        var exposed = CreateToolResultMessage("call_missing", "result-tool", "payload");
        var protectedMessage = ContextMessage.FromText(MessageRole.Model, "recent");
        var messages = new List<ContextMessage> { exposed, protectedMessage };

        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(protectedMessage, 6);
        tokenCounter.Set(exposed, 6);

        var strategy = new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 2, protectedWindowFraction: 0.50));

        // Act
        var compacted = await strategy.CompactAsync(messages, ContextBudget.For(10), tokenCounter);

        // Assert
        var text = Assert.IsType<TextContent>(Assert.Single(compacted[0].Content));
        Assert.Equal("[Tool result cleared — call_missing, call_missing]", text.Text);
    }

    [Fact]
    public async Task CompactAsync_DoesNotMutateInputListOrInputMessages()
    {
        // Arrange
        var originalMessage = new ContextMessage
        {
            Role = MessageRole.User,
            Content = [new ToolResultContent("call_1", "tool", "payload")],
            State = CompactionState.Original,
        };

        var protectedMessage = ContextMessage.FromText(MessageRole.Model, "recent");
        var messages = new List<ContextMessage> { originalMessage, protectedMessage };

        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(protectedMessage, 6);
        tokenCounter.Set(originalMessage, 6);

        var strategy = new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 2, protectedWindowFraction: 0.50));

        // Act
        var compacted = await strategy.CompactAsync(messages, ContextBudget.For(10), tokenCounter);

        // Assert
        Assert.Equal(2, messages.Count);
        Assert.Same(originalMessage, messages[0]);
        Assert.Equal(CompactionState.Original, originalMessage.State);
        Assert.IsType<ToolResultContent>(originalMessage.Content[0]);
        Assert.NotSame(originalMessage, compacted[0]);
    }

    [Fact]
    public void SlidingWindowOptions_Default_ReturnsExpectedValues()
    {
        // Arrange

        // Act
        var options = SlidingWindowOptions.Default;

        // Assert
        Assert.Equal(10, options.WindowSize);
        Assert.Equal(0.40, options.ProtectedWindowFraction);
        Assert.Equal("[Tool result cleared — {0}, {1}]", options.PlaceholderFormat);
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
            Content = [new ToolResultContent(toolCallId, toolName, payload)],
        };
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
