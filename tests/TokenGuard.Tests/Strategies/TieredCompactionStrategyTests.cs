using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;
using TokenGuard.Core.Options;
using TokenGuard.Core.Strategies;

namespace TokenGuard.Tests.Strategies;

public sealed class TieredCompactionStrategyTests
{
    [Fact]
    public async Task CompactAsync_WhenSlidingWindowFitsBudget_DoesNotCallSummarizer()
    {
        // Arrange
        var oldest = CreateToolResultMessage("call_1", "search", "tool-output");
        var keep1 = ContextMessage.FromText(MessageRole.User, "keep-1");
        var keep2 = ContextMessage.FromText(MessageRole.Model, "keep-2");
        var messages = new List<ContextMessage> { oldest, keep1, keep2 };

        var summarizer = new TrackingSummarizer("summary-text");
        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(oldest, 10);
        tokenCounter.Set(keep1, 4);
        tokenCounter.Set(keep2, 4);

        var strategy = new TieredCompactionStrategy(
            summarizer,
            new TieredCompactionOptions(
                new SlidingWindowOptions(windowSize: 2, protectedWindowFraction: 0.20),
                new LlmSummarizationOptions(windowSize: 2)));

        // Act
        var compacted = await strategy.CompactAsync(messages, 10, tokenCounter);

        // Assert
        Assert.Equal(0, summarizer.CallCount);
        Assert.Equal(nameof(TieredCompactionStrategy), compacted.StrategyName);
        Assert.Equal(1, compacted.MessagesAffected);
        Assert.Equal(CompactionState.Masked, compacted.Messages[0].State);
        Assert.Same(keep1, compacted.Messages[1]);
        Assert.Same(keep2, compacted.Messages[2]);
    }

    [Fact]
    public async Task CompactAsync_WhenSlidingWindowExceedsBudget_CallsSummarizerWithOriginalMessages()
    {
        // Arrange — two old tool-result messages so that masking (both → 1 token each) still leaves
        // total=12 > availableTokens=11, forcing escalation to summarization.
        var oldest1 = CreateToolResultMessage("call_1", "search", "full-tool-output-1");
        var oldest2 = CreateToolResultMessage("call_2", "search", "full-tool-output-2");
        var keep1 = ContextMessage.FromText(MessageRole.User, "keep-1");
        var keep2 = ContextMessage.FromText(MessageRole.Model, "keep-2");
        var messages = new List<ContextMessage> { oldest1, oldest2, keep1, keep2 };

        var summarizer = new TrackingSummarizer("summary-text");
        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(oldest1, 50);
        tokenCounter.Set(oldest2, 50);
        tokenCounter.Set(keep1, 5);
        tokenCounter.Set(keep2, 5);

        var strategy = new TieredCompactionStrategy(
            summarizer,
            new TieredCompactionOptions(
                new SlidingWindowOptions(windowSize: 1, protectedWindowFraction: 0.20),
                new LlmSummarizationOptions(windowSize: 2, minSummaryTokens: 1, maxSummaryTokens: 100)));

        // Act — masked total: 1+1+5+5=12 > 11, so sliding-window fails; remaining budget 11-10=1 ≥ 1
        var compacted = await strategy.CompactAsync(messages, 11, tokenCounter);

        // Assert
        Assert.Equal(1, summarizer.CallCount);
        Assert.Equal([oldest1, oldest2], summarizer.LastMessages);
        var summarizedToolResult = Assert.IsType<ToolResultContent>(Assert.Single(summarizer.LastMessages[0].Segments));
        Assert.Equal("full-tool-output-1", summarizedToolResult.Content);
        Assert.Equal(nameof(TieredCompactionStrategy), compacted.StrategyName);
    }

    [Fact]
    public async Task CompactAsync_WhenBothStagesAreNoOp_ReturnsOriginalMessages()
    {
        // Arrange
        var first = ContextMessage.FromText(MessageRole.User, "one");
        var second = ContextMessage.FromText(MessageRole.Model, "two");
        var messages = new List<ContextMessage> { first, second };

        var summarizer = new TrackingSummarizer("unused");
        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(first, 2);
        tokenCounter.Set(second, 3);

        var strategy = new TieredCompactionStrategy(
            summarizer,
            new TieredCompactionOptions(
                new SlidingWindowOptions(windowSize: 5, protectedWindowFraction: 0.90),
                new LlmSummarizationOptions(windowSize: 5)));

        // Act
        var compacted = await strategy.CompactAsync(messages, 100, tokenCounter);

        // Assert
        Assert.Same(messages, compacted.Messages);
        Assert.Equal(0, compacted.MessagesAffected);
        Assert.Equal(0, summarizer.CallCount);
        Assert.Equal(nameof(TieredCompactionStrategy), compacted.StrategyName);
    }

    [Fact]
    public async Task CompactAsync_WhenSummaryStageCannotCompact_ReturnsNoOpResult()
    {
        // Arrange
        var oldest = CreateToolResultMessage("call_1", "search", "tool-output");
        var keep = ContextMessage.FromText(MessageRole.User, "keep");
        var messages = new List<ContextMessage> { oldest, keep };

        var summarizer = new TrackingSummarizer("unused");
        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(oldest, 10);
        tokenCounter.Set(keep, 8);

        var strategy = new TieredCompactionStrategy(
            summarizer,
            new TieredCompactionOptions(
                new SlidingWindowOptions(windowSize: 1, protectedWindowFraction: 0.20),
                new LlmSummarizationOptions(windowSize: 5)));

        // Act
        var compacted = await strategy.CompactAsync(messages, 5, tokenCounter);

        // Assert
        Assert.Equal(0, summarizer.CallCount);
        Assert.Equal(nameof(TieredCompactionStrategy), compacted.StrategyName);
        Assert.Same(messages, compacted.Messages);
        Assert.Equal(0, compacted.MessagesAffected);
        Assert.Equal(18, compacted.TokensBefore);
        Assert.Equal(18, compacted.TokensAfter);
    }

    [Fact]
    public async Task CompactAsync_WhenSummarizationFires_ReturnsSummaryMessageFollowedByProtectedTail()
    {
        // Arrange — same 4-message layout as the "calls summarizer with original messages" test.
        var oldest1 = CreateToolResultMessage("call_1", "search", "full-tool-output-1");
        var oldest2 = CreateToolResultMessage("call_2", "search", "full-tool-output-2");
        var keep1 = ContextMessage.FromText(MessageRole.User, "keep-1");
        var keep2 = ContextMessage.FromText(MessageRole.Model, "keep-2");
        var messages = new List<ContextMessage> { oldest1, oldest2, keep1, keep2 };

        var summarizer = new TrackingSummarizer("summary-text");
        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(oldest1, 50);
        tokenCounter.Set(oldest2, 50);
        tokenCounter.Set(keep1, 5);
        tokenCounter.Set(keep2, 5);

        var strategy = new TieredCompactionStrategy(
            summarizer,
            new TieredCompactionOptions(
                new SlidingWindowOptions(windowSize: 1, protectedWindowFraction: 0.20),
                new LlmSummarizationOptions(windowSize: 2, minSummaryTokens: 1, maxSummaryTokens: 100)));

        // Act — masked total: 1+1+5+5=12 > 11, so sliding-window fails; remaining budget 11-10=1 ≥ 1
        var compacted = await strategy.CompactAsync(messages, 11, tokenCounter);

        // Assert
        Assert.Equal(3, compacted.Messages.Count);

        var summaryMessage = compacted.Messages[0];
        Assert.Equal(MessageRole.User, summaryMessage.Role);
        Assert.Equal(CompactionState.Summarized, summaryMessage.State);
        Assert.Equal("summary-text", Assert.IsType<TextContent>(Assert.Single(summaryMessage.Segments)).Content);

        Assert.Same(keep1, compacted.Messages[1]);
        Assert.Same(keep2, compacted.Messages[2]);
    }

    [Fact]
    public async Task CompactAsync_WhenSummarizationBudgetBelowMinimum_SkipsCallAndReturnsProtectedTailOnly()
    {
        // Arrange
        var oldest = ContextMessage.FromText(MessageRole.User, "old");
        var keep1 = ContextMessage.FromText(MessageRole.User, "keep-1");
        var keep2 = ContextMessage.FromText(MessageRole.Model, "keep-2");
        var messages = new List<ContextMessage> { oldest, keep1, keep2 };

        var summarizer = new TrackingSummarizer("summary-text");
        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(oldest, 10);
        tokenCounter.Set(keep1, 8);
        tokenCounter.Set(keep2, 8);

        var strategy = new TieredCompactionStrategy(
            summarizer,
            new TieredCompactionOptions(
                new SlidingWindowOptions(windowSize: 1, protectedWindowFraction: 0.20),
                new LlmSummarizationOptions(windowSize: 2, minSummaryTokens: 500, maxSummaryTokens: 1000)));

        // Act — remainingBudget = 10 - 16 = -6, below minSummaryTokens=500; summarization skipped
        var compacted = await strategy.CompactAsync(messages, 10, tokenCounter);

        // Assert
        Assert.Equal(0, summarizer.CallCount);
        Assert.Equal(nameof(TieredCompactionStrategy), compacted.StrategyName);
        Assert.Equal(1, compacted.MessagesAffected);
        Assert.Equal(2, compacted.Messages.Count);
        Assert.Same(keep1, compacted.Messages[0]);
        Assert.Same(keep2, compacted.Messages[1]);
    }

    [Fact]
    public async Task CompactAsync_UsesTieredStrategyNameInAllBranches()
    {
        // Arrange
        var noOpMessages = new List<ContextMessage>
        {
            ContextMessage.FromText(MessageRole.User, "one"),
            ContextMessage.FromText(MessageRole.Model, "two"),
        };

        var slidingOnlyMessages = new List<ContextMessage>
        {
            CreateToolResultMessage("call_2", "search", "tool-output"),
            ContextMessage.FromText(MessageRole.User, "keep-1"),
            ContextMessage.FromText(MessageRole.Model, "keep-2"),
        };

        var summarizationMessages = new List<ContextMessage>
        {
            CreateToolResultMessage("call_3", "search", "tool-output"),
            ContextMessage.FromText(MessageRole.User, "keep-1"),
            ContextMessage.FromText(MessageRole.Model, "keep-2"),
        };

        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(noOpMessages[0], 2);
        tokenCounter.Set(noOpMessages[1], 2);
        tokenCounter.Set(slidingOnlyMessages[0], 10);
        tokenCounter.Set(slidingOnlyMessages[1], 4);
        tokenCounter.Set(slidingOnlyMessages[2], 4);
        tokenCounter.Set(summarizationMessages[0], 10);
        tokenCounter.Set(summarizationMessages[1], 8);
        tokenCounter.Set(summarizationMessages[2], 8);

        var summarizer = new TrackingSummarizer("summary-text");
        var noOpStrategy = new TieredCompactionStrategy(
            summarizer,
            new TieredCompactionOptions(
                new SlidingWindowOptions(windowSize: 5, protectedWindowFraction: 0.90),
                new LlmSummarizationOptions(windowSize: 5)));
        var slidingOnlyStrategy = new TieredCompactionStrategy(
            summarizer,
            new TieredCompactionOptions(
                new SlidingWindowOptions(windowSize: 2, protectedWindowFraction: 0.20),
                new LlmSummarizationOptions(windowSize: 2)));
        var summarizationStrategy = new TieredCompactionStrategy(
            summarizer,
            new TieredCompactionOptions(
                new SlidingWindowOptions(windowSize: 1, protectedWindowFraction: 0.20),
                new LlmSummarizationOptions(windowSize: 2)));

        // Act
        var noOpResult = await noOpStrategy.CompactAsync(noOpMessages, 100, tokenCounter);
        var slidingOnlyResult = await slidingOnlyStrategy.CompactAsync(slidingOnlyMessages, 10, tokenCounter);
        var summarizationResult = await summarizationStrategy.CompactAsync(summarizationMessages, 10, tokenCounter);

        // Assert
        Assert.Equal(nameof(TieredCompactionStrategy), noOpResult.StrategyName);
        Assert.Equal(nameof(TieredCompactionStrategy), slidingOnlyResult.StrategyName);
        Assert.Equal(nameof(TieredCompactionStrategy), summarizationResult.StrategyName);
    }

    [Fact]
    public void TieredCompactionOptions_Default_UsesEmbeddedStrategyDefaults()
    {
        // Arrange

        // Act
        var options = TieredCompactionOptions.Default;

        // Assert
        Assert.Equal(SlidingWindowOptions.Default, options.SlidingWindowOptions);
        Assert.Equal(LlmSummarizationOptions.Default, options.LlmSummarizationOptions);
    }

    private static ContextMessage CreateToolResultMessage(string callId, string toolName, string payload)
    {
        return new ContextMessage
        {
            Role = MessageRole.User,
            Segments = [new ToolResultContent(callId, toolName, payload)],
        };
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
