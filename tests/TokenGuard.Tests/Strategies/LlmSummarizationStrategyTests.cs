using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;
using TokenGuard.Core.Options;
using TokenGuard.Core.Strategies;

namespace TokenGuard.Tests.Strategies;

public sealed class LlmSummarizationStrategyTests
{
    [Fact]
    public async Task CompactAsync_WithEmptyMessageList_ReturnsEmptyList()
    {
        // Arrange
        IReadOnlyList<ContextMessage> messages = [];
        var summarizer = new TrackingSummarizer("unused");
        var tokenCounter = new TrackingTokenCounter();
        var strategy = new LlmSummarizationStrategy(summarizer, tokenCounter);

        // Act
        var compacted = await strategy.CompactAsync(messages, 100);

        // Assert
        Assert.Same(messages, compacted.Messages);
        Assert.Empty(compacted.Messages);
        Assert.Equal(0, compacted.TokensBefore);
        Assert.Equal(0, compacted.TokensAfter);
        Assert.Equal(0, compacted.MessagesAffected);
        Assert.Equal(nameof(LlmSummarizationStrategy), compacted.StrategyName);
        Assert.Equal(0, summarizer.CallCount);
    }

    [Fact]
    public async Task CompactAsync_WhenAllMessagesFitWithinProtectedWindow_ReturnsOriginalListReference()
    {
        // Arrange
        var messages = new List<ContextMessage>
        {
            ContextMessage.FromText(MessageRole.User, "one"),
            ContextMessage.FromText(MessageRole.Model, "two"),
        };

        var summarizer = new TrackingSummarizer("unused");
        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(messages[0], 2);
        tokenCounter.Set(messages[1], 3);

        var strategy = new LlmSummarizationStrategy(summarizer, tokenCounter, new LlmSummarizationOptions(windowSize: 5));

        // Act
        var compacted = await strategy.CompactAsync(messages, 100);

        // Assert
        Assert.Same(messages, compacted.Messages);
        Assert.Equal(5, compacted.TokensBefore);
        Assert.Equal(5, compacted.TokensAfter);
        Assert.Equal(0, compacted.MessagesAffected);
        Assert.Equal(0, summarizer.CallCount);
    }

    [Fact]
    public async Task CompactAsync_WhenSummarizationTriggers_InsertsSummaryMessageBeforeProtectedTail()
    {
        // Arrange
        var oldest = ContextMessage.FromText(MessageRole.User, "old-1");
        var older = ContextMessage.FromText(MessageRole.Model, "old-2");
        var keep1 = ContextMessage.FromText(MessageRole.User, "keep-1");
        var keep2 = ContextMessage.FromText(MessageRole.Model, "keep-2");
        var messages = new List<ContextMessage> { oldest, older, keep1, keep2 };

        var summarizer = new TrackingSummarizer("summary-text");
        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(oldest, 4);
        tokenCounter.Set(older, 5);
        tokenCounter.Set(keep1, 6);
        tokenCounter.Set(keep2, 7);

        var strategy = new LlmSummarizationStrategy(
            summarizer,
            tokenCounter,
            new LlmSummarizationOptions(windowSize: 2, minSummaryTokens: 1, maxSummaryTokens: 100));

        // Act
        var compacted = await strategy.CompactAsync(messages, 20);

        // Assert
        Assert.Equal(1, summarizer.CallCount);
        Assert.Equal([oldest, older], summarizer.LastMessages);
        Assert.Equal(7, summarizer.LastTargetTokens);

        var summaryMessage = compacted.Messages[0];
        Assert.Equal(MessageRole.User, summaryMessage.Role);
        Assert.Equal(CompactionState.Summarized, summaryMessage.State);
        Assert.Equal("summary-text", Assert.IsType<TextContent>(Assert.Single(summaryMessage.Segments)).Content);

        Assert.Same(keep1, compacted.Messages[1]);
        Assert.Same(keep2, compacted.Messages[2]);
        Assert.Equal(2, compacted.MessagesAffected);
        Assert.Equal(nameof(LlmSummarizationStrategy), compacted.StrategyName);
    }

    [Fact]
    public async Task CompactAsync_WhenRemainingBudgetBelowMinimum_SkipsSummarizationAndReturnsProtectedTailOnly()
    {
        // Arrange
        var summarize = ContextMessage.FromText(MessageRole.User, "old");
        var keep1 = ContextMessage.FromText(MessageRole.User, "keep-1");
        var keep2 = ContextMessage.FromText(MessageRole.Model, "keep-2");
        var messages = new List<ContextMessage> { summarize, keep1, keep2 };

        var summarizer = new TrackingSummarizer("summary-text");
        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(summarize, 2);
        tokenCounter.Set(keep1, 8);
        tokenCounter.Set(keep2, 7);

        var strategy = new LlmSummarizationStrategy(
            summarizer,
            tokenCounter,
            new LlmSummarizationOptions(windowSize: 2, minSummaryTokens: 50, maxSummaryTokens: 100));

        // Act
        var compacted = await strategy.CompactAsync(messages, 10);

        // Assert
        Assert.Equal(0, summarizer.CallCount);
        Assert.Equal(1, compacted.MessagesAffected);
        Assert.Equal(17, compacted.TokensBefore);
        Assert.Equal(15, compacted.TokensAfter);
        Assert.Equal(2, compacted.Messages.Count);
        Assert.Same(keep1, compacted.Messages[0]);
        Assert.Same(keep2, compacted.Messages[1]);
    }

    [Fact]
    public async Task CompactAsync_RepeatedCallsWithSameMessages_InvokeSummarizerEachTime()
    {
        // Arrange
        var messages = new List<ContextMessage>
        {
            ContextMessage.FromText(MessageRole.User, "old"),
            ContextMessage.FromText(MessageRole.Model, "keep"),
        };

        var summarizer = new TrackingSummarizer("summary-text");
        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(messages[0], 4);
        tokenCounter.Set(messages[1], 4);

        var strategy = new LlmSummarizationStrategy(
            summarizer,
            tokenCounter,
            new LlmSummarizationOptions(windowSize: 1, minSummaryTokens: 1, maxSummaryTokens: 100));

        // Act
        _ = await strategy.CompactAsync(messages, 10);
        _ = await strategy.CompactAsync(messages, 10);

        // Assert
        Assert.Equal(2, summarizer.CallCount);
    }

    [Fact]
    public async Task LlmSummarizationStrategy_SingleArgumentConstructor_UsesDefaultOptions()
    {
        // Arrange
        var summarizer = new TrackingSummarizer("summary-text");
        var messages = new List<ContextMessage>
        {
            ContextMessage.FromText(MessageRole.User, "old-1"),
            ContextMessage.FromText(MessageRole.Model, "old-2"),
            ContextMessage.FromText(MessageRole.User, "keep-1"),
            ContextMessage.FromText(MessageRole.Model, "keep-2"),
            ContextMessage.FromText(MessageRole.User, "keep-3"),
            ContextMessage.FromText(MessageRole.Model, "keep-4"),
        };

        var tokenCounter = new TrackingTokenCounter();

        // Act
        var strategy = new LlmSummarizationStrategy(summarizer, tokenCounter);
        var compacted = await strategy.CompactAsync(messages, 3000);

        // Assert
        Assert.Equal(1, summarizer.CallCount);
        Assert.Single(summarizer.LastMessages);
        Assert.Equal(6, compacted.Messages.Count);
        Assert.Equal(1, compacted.MessagesAffected);
    }

    [Fact]
    public void LlmSummarizationOptions_Default_ReturnsExpectedValues()
    {
        // Arrange

        // Act
        var options = LlmSummarizationOptions.Default;

        // Assert
        Assert.Equal(5, options.WindowSize);
        Assert.Equal(2048, options.MinSummaryTokens);
        Assert.Equal(4096, options.MaxSummaryTokens);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void LlmSummarizationOptions_ThrowsForInvalidWindowSize(int windowSize)
    {
        // Arrange

        // Act
        Action act = () => _ = new LlmSummarizationOptions(windowSize);

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(act);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(-1, 100)]
    public void LlmSummarizationOptions_ThrowsForInvalidMinSummaryTokens(int minSummaryTokens, int maxSummaryTokens)
    {
        // Arrange

        // Act
        Action act = () => _ = new LlmSummarizationOptions(
            windowSize: 5,
            minSummaryTokens: minSummaryTokens,
            maxSummaryTokens: maxSummaryTokens);

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(act);
    }

    [Theory]
    [InlineData(100, 0)]
    [InlineData(100, -1)]
    [InlineData(100, 99)]
    public void LlmSummarizationOptions_ThrowsForInvalidMaxSummaryTokens(int minSummaryTokens, int maxSummaryTokens)
    {
        // Arrange

        // Act
        Action act = () => _ = new LlmSummarizationOptions(
            windowSize: 5,
            minSummaryTokens: minSummaryTokens,
            maxSummaryTokens: maxSummaryTokens);

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(act);
    }

    [Fact]
    public void LlmSummarizationOptions_DefaultConstructor_ProducesValidDefaultValues()
    {
        // Arrange

        // Act
        var options = new LlmSummarizationOptions();

        // Assert
        Assert.Equal(LlmSummarizationOptions.Default, options);
    }

    [Fact]
    public async Task CompactAsync_WhenRemainingBudgetExceedsMax_ClampsTargetToMax()
    {
        // Arrange
        var old = ContextMessage.FromText(MessageRole.User, "old");
        var keep = ContextMessage.FromText(MessageRole.Model, "keep");
        var messages = new List<ContextMessage> { old, keep };

        var summarizer = new TrackingSummarizer("summary-text");
        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(old, 10);
        tokenCounter.Set(keep, 5);

        var strategy = new LlmSummarizationStrategy(
            summarizer,
            tokenCounter,
            new LlmSummarizationOptions(windowSize: 1, minSummaryTokens: 10, maxSummaryTokens: 50));

        // Act — remainingBudget = 200 - 5 = 195, which exceeds max of 50
        _ = await strategy.CompactAsync(messages, 200);

        // Assert
        Assert.Equal(50, summarizer.LastTargetTokens);
    }

    [Fact]
    public async Task CompactAsync_WhenRemainingBudgetWithinRange_PassesExactBudget()
    {
        // Arrange
        var old = ContextMessage.FromText(MessageRole.User, "old");
        var keep = ContextMessage.FromText(MessageRole.Model, "keep");
        var messages = new List<ContextMessage> { old, keep };

        var summarizer = new TrackingSummarizer("summary-text");
        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(old, 10);
        tokenCounter.Set(keep, 5);

        var strategy = new LlmSummarizationStrategy(
            summarizer,
            tokenCounter,
            new LlmSummarizationOptions(windowSize: 1, minSummaryTokens: 10, maxSummaryTokens: 100));

        // Act — remainingBudget = 55 - 5 = 50, within [10, 100]
        _ = await strategy.CompactAsync(messages, 55);

        // Assert
        Assert.Equal(50, summarizer.LastTargetTokens);
    }

    [Fact]
    public async Task CompactAsync_WhenRemainingBudgetEqualsMin_CallsSummarizer()
    {
        // Arrange
        var old = ContextMessage.FromText(MessageRole.User, "old");
        var keep = ContextMessage.FromText(MessageRole.Model, "keep");
        var messages = new List<ContextMessage> { old, keep };

        var summarizer = new TrackingSummarizer("summary-text");
        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(old, 10);
        tokenCounter.Set(keep, 5);

        var strategy = new LlmSummarizationStrategy(
            summarizer,
            tokenCounter,
            new LlmSummarizationOptions(windowSize: 1, minSummaryTokens: 20, maxSummaryTokens: 100));

        // Act — remainingBudget = 25 - 5 = 20, exactly equals min
        _ = await strategy.CompactAsync(messages, 25);

        // Assert
        Assert.Equal(1, summarizer.CallCount);
        Assert.Equal(20, summarizer.LastTargetTokens);
    }

    [Fact]
    public async Task CompactAsync_WhenRemainingBudgetEqualsMax_ClampsToMax()
    {
        // Arrange
        var old = ContextMessage.FromText(MessageRole.User, "old");
        var keep = ContextMessage.FromText(MessageRole.Model, "keep");
        var messages = new List<ContextMessage> { old, keep };

        var summarizer = new TrackingSummarizer("summary-text");
        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(old, 10);
        tokenCounter.Set(keep, 5);

        var strategy = new LlmSummarizationStrategy(
            summarizer,
            tokenCounter,
            new LlmSummarizationOptions(windowSize: 1, minSummaryTokens: 10, maxSummaryTokens: 50));

        // Act — remainingBudget = 55 - 5 = 50, exactly equals max
        _ = await strategy.CompactAsync(messages, 55);

        // Assert
        Assert.Equal(1, summarizer.CallCount);
        Assert.Equal(50, summarizer.LastTargetTokens);
    }

    private sealed class TrackingSummarizer : ILlmSummarizer
    {
        private readonly string _summary;

        public TrackingSummarizer(string summary)
        {
            this._summary = summary;
        }

        public int CallCount { get; private set; }

        public IReadOnlyList<ContextMessage> LastMessages { get; private set; } = [];

        public int LastTargetTokens { get; private set; }

        public Task<string> SummarizeAsync(
            IReadOnlyList<ContextMessage> messages,
            int targetTokens,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            this.CallCount++;
            this.LastMessages = messages.ToArray();
            this.LastTargetTokens = targetTokens;

            return Task.FromResult(this._summary);
        }
    }

    private sealed class TrackingTokenCounter : ITokenCounter
    {
        private readonly Dictionary<ContextMessage, int> _counts = new(ReferenceEqualityComparer.Instance);

        public void Set(ContextMessage contextMessage, int count)
        {
            this._counts[contextMessage] = count;
        }

        public int Count(ContextMessage contextMessage)
        {
            return this._counts.TryGetValue(contextMessage, out var value)
                ? value
                : 1;
        }

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
        public static readonly ReferenceEqualityComparer Instance = new();

        public bool Equals(ContextMessage? x, ContextMessage? y)
        {
            return ReferenceEquals(x, y);
        }

        public int GetHashCode(ContextMessage obj)
        {
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
