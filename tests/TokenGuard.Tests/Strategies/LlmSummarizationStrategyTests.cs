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
        var first = await strategy.CompactAsync(messages, 10);
        var second = await strategy.CompactAsync(messages, 10);

        // Assert
        Assert.Equal(1, summarizer.CallCount);
        Assert.Equal(first.Messages.Count, second.Messages.Count);
        Assert.Equal(ReadText(first.Messages[0]), ReadText(second.Messages[0]));
        Assert.Same(first.Messages[1], second.Messages[1]);
    }

    [Fact]
    public async Task CompactAsync_TailGrowth_ReusesCheckpointWithZeroNewCalls()
    {
        // Arrange
        var a = ContextMessage.FromText(MessageRole.User, "A");
        var b = ContextMessage.FromText(MessageRole.Model, "B");
        var c = ContextMessage.FromText(MessageRole.User, "C");
        var d = ContextMessage.FromText(MessageRole.Model, "D");
        var e = ContextMessage.FromText(MessageRole.User, "E");

        var summarizer = new TrackingSummarizer(invocation => Task.FromResult($"summary-{invocation.CallNumber}"));
        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(a, 2);
        tokenCounter.Set(b, 2);
        tokenCounter.Set(c, 2);
        tokenCounter.Set(d, 2);
        tokenCounter.Set(e, 2);
        tokenCounter.SetByText("summary-1", 1);

        var strategy = new LlmSummarizationStrategy(
            summarizer,
            tokenCounter,
            new LlmSummarizationOptions(windowSize: 2, minSummaryTokens: 1, maxSummaryTokens: 100));

        // Act
        _ = await strategy.CompactAsync([a, b, c, d], 8);
        var compacted = await strategy.CompactAsync([a, b, c, d, e], 8);

        // Assert
        Assert.Equal(1, summarizer.CallCount);
        Assert.Collection(
            compacted.Messages,
            summary => Assert.Equal("summary-1", ReadText(summary)),
            message => Assert.Same(c, message),
            message => Assert.Same(d, message),
            message => Assert.Same(e, message));
    }

    [Fact]
    public async Task CompactAsync_OverflowAfterTailGrowth_PromotesCheckpointWithOneNewCall()
    {
        // Arrange
        var a = ContextMessage.FromText(MessageRole.User, "A");
        var b = ContextMessage.FromText(MessageRole.Model, "B");
        var c = ContextMessage.FromText(MessageRole.User, "C");
        var d = ContextMessage.FromText(MessageRole.Model, "D");
        var e = ContextMessage.FromText(MessageRole.User, "E");

        var summarizer = new TrackingSummarizer(invocation => Task.FromResult($"summary-{invocation.CallNumber}"));
        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(a, 2);
        tokenCounter.Set(b, 2);
        tokenCounter.Set(c, 2);
        tokenCounter.Set(d, 2);
        tokenCounter.Set(e, 2);
        tokenCounter.SetByText("summary-1", 1);
        tokenCounter.SetByText("summary-2", 1);

        var strategy = new LlmSummarizationStrategy(
            summarizer,
            tokenCounter,
            new LlmSummarizationOptions(windowSize: 2, minSummaryTokens: 1, maxSummaryTokens: 100));

        // Act
        _ = await strategy.CompactAsync([a, b, c, d], 6);
        var compacted = await strategy.CompactAsync([a, b, c, d, e], 6);

        // Assert
        Assert.Equal(2, summarizer.CallCount);
        Assert.Equal([a, b, c], summarizer.Calls[1].Messages);
        Assert.Collection(
            compacted.Messages,
            summary => Assert.Equal("summary-2", ReadText(summary)),
            message => Assert.Same(d, message),
            message => Assert.Same(e, message));
    }

    [Fact]
    public async Task CompactAsync_DoublePromotion_TriggersOneCallPerPromotion()
    {
        // Arrange
        var a = ContextMessage.FromText(MessageRole.User, "A");
        var b = ContextMessage.FromText(MessageRole.Model, "B");
        var c = ContextMessage.FromText(MessageRole.User, "C");
        var d = ContextMessage.FromText(MessageRole.Model, "D");
        var e = ContextMessage.FromText(MessageRole.User, "E");
        var f = ContextMessage.FromText(MessageRole.Model, "F");

        var summarizer = new TrackingSummarizer(invocation => Task.FromResult($"summary-{invocation.CallNumber}"));
        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(a, 2);
        tokenCounter.Set(b, 2);
        tokenCounter.Set(c, 2);
        tokenCounter.Set(d, 2);
        tokenCounter.Set(e, 2);
        tokenCounter.Set(f, 2);
        tokenCounter.SetByText("summary-1", 1);
        tokenCounter.SetByText("summary-2", 1);
        tokenCounter.SetByText("summary-3", 1);

        var strategy = new LlmSummarizationStrategy(
            summarizer,
            tokenCounter,
            new LlmSummarizationOptions(windowSize: 2, minSummaryTokens: 1, maxSummaryTokens: 100));

        // Act
        _ = await strategy.CompactAsync([a, b, c, d], 6);
        _ = await strategy.CompactAsync([a, b, c, d, e], 6);
        _ = await strategy.CompactAsync([a, b, c, d, e], 6);
        var compacted = await strategy.CompactAsync([a, b, c, d, e, f], 6);

        // Assert
        Assert.Equal(3, summarizer.CallCount);
        Assert.Equal([a, b], summarizer.Calls[0].Messages);
        Assert.Equal([a, b, c], summarizer.Calls[1].Messages);
        Assert.Equal([a, b, c, d], summarizer.Calls[2].Messages);
        Assert.Collection(
            compacted.Messages,
            summary => Assert.Equal("summary-3", ReadText(summary)),
            message => Assert.Same(e, message),
            message => Assert.Same(f, message));
    }

    [Fact]
    public async Task CompactAsync_UnrelatedConversation_InvalidatesCheckpointAndResummarizes()
    {
        // Arrange
        var a = ContextMessage.FromText(MessageRole.User, "A");
        var x = ContextMessage.FromText(MessageRole.User, "X");
        var keep = ContextMessage.FromText(MessageRole.Model, "keep");

        var summarizer = new TrackingSummarizer(invocation => Task.FromResult($"summary-{invocation.CallNumber}"));
        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(a, 2);
        tokenCounter.Set(x, 2);
        tokenCounter.Set(keep, 2);

        var strategy = new LlmSummarizationStrategy(
            summarizer,
            tokenCounter,
            new LlmSummarizationOptions(windowSize: 1, minSummaryTokens: 1, maxSummaryTokens: 100));

        // Act
        _ = await strategy.CompactAsync([a, keep], 10);
        var compacted = await strategy.CompactAsync([x, keep], 10);

        // Assert
        Assert.Equal(2, summarizer.CallCount);
        Assert.Equal([x], summarizer.Calls[1].Messages);
        Assert.Equal("summary-2", ReadText(compacted.Messages[0]));
    }

    [Fact]
    public async Task CompactAsync_TruncatedHistory_InvalidatesCheckpointAndReturnsRawMessages()
    {
        // Arrange
        var a = ContextMessage.FromText(MessageRole.User, "A");
        var b = ContextMessage.FromText(MessageRole.Model, "B");
        var c = ContextMessage.FromText(MessageRole.User, "C");
        var d = ContextMessage.FromText(MessageRole.Model, "D");
        var e = ContextMessage.FromText(MessageRole.User, "E");

        var summarizer = new TrackingSummarizer("summary");
        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(a, 2);
        tokenCounter.Set(b, 2);
        tokenCounter.Set(c, 2);
        tokenCounter.Set(d, 2);
        tokenCounter.Set(e, 2);

        var strategy = new LlmSummarizationStrategy(
            summarizer,
            tokenCounter,
            new LlmSummarizationOptions(windowSize: 2, minSummaryTokens: 1, maxSummaryTokens: 100));

        // Act
        _ = await strategy.CompactAsync([a, b, c, d, e], 10);
        var truncated = new List<ContextMessage> { a, b };
        var compacted = await strategy.CompactAsync(truncated, 10);

        // Assert
        Assert.Equal(1, summarizer.CallCount);
        Assert.Same(truncated, compacted.Messages);
        var checkpoint = ReadCheckpoint(strategy);
        Assert.Equal(0, checkpoint.CoveredCount);
        Assert.Equal(0L, checkpoint.Fingerprint);
        Assert.Null(checkpoint.Summary);
    }

    [Fact]
    public async Task CompactAsync_SummarizerThrowsOnFirstCall_LeavesCheckpointAtDefaults()
    {
        // Arrange
        var old = ContextMessage.FromText(MessageRole.User, "old");
        var keep = ContextMessage.FromText(MessageRole.Model, "keep");

        var summarizer = new TrackingSummarizer(invocation => invocation.CallNumber == 1
            ? Task.FromException<string>(new InvalidOperationException("boom"))
            : Task.FromResult("summary-2"));

        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(old, 4);
        tokenCounter.Set(keep, 4);

        var strategy = new LlmSummarizationStrategy(
            summarizer,
            tokenCounter,
            new LlmSummarizationOptions(windowSize: 1, minSummaryTokens: 1, maxSummaryTokens: 100));

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() => strategy.CompactAsync([old, keep], 10));
        var afterFailure = ReadCheckpoint(strategy);
        _ = await strategy.CompactAsync([old, keep], 10);
        var afterSuccess = ReadCheckpoint(strategy);

        // Assert
        Assert.Equal(0, afterFailure.CoveredCount);
        Assert.Equal(0L, afterFailure.Fingerprint);
        Assert.Null(afterFailure.Summary);
        Assert.Equal(2, summarizer.CallCount);
        Assert.Equal(1, afterSuccess.CoveredCount);
        Assert.NotNull(afterSuccess.Summary);
    }

    [Fact]
    public async Task CompactAsync_SummarizerThrowsDuringPromotion_PreservesOriginalCheckpoint()
    {
        // Arrange
        var a = ContextMessage.FromText(MessageRole.User, "A");
        var b = ContextMessage.FromText(MessageRole.Model, "B");
        var c = ContextMessage.FromText(MessageRole.User, "C");
        var d = ContextMessage.FromText(MessageRole.Model, "D");
        var e = ContextMessage.FromText(MessageRole.User, "E");

        var summarizer = new TrackingSummarizer(invocation => invocation.CallNumber switch
        {
            1 => Task.FromResult("summary-1"),
            2 => Task.FromException<string>(new InvalidOperationException("promotion failed")),
            _ => Task.FromResult("summary-3"),
        });

        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(a, 2);
        tokenCounter.Set(b, 2);
        tokenCounter.Set(c, 2);
        tokenCounter.Set(d, 2);
        tokenCounter.Set(e, 2);
        tokenCounter.SetByText("summary-1", 1);
        tokenCounter.SetByText("summary-3", 1);

        var strategy = new LlmSummarizationStrategy(
            summarizer,
            tokenCounter,
            new LlmSummarizationOptions(windowSize: 2, minSummaryTokens: 1, maxSummaryTokens: 100));

        // Act
        _ = await strategy.CompactAsync([a, b, c, d], 6);
        var checkpointBeforePromotion = ReadCheckpoint(strategy);
        await Assert.ThrowsAsync<InvalidOperationException>(() => strategy.CompactAsync([a, b, c, d, e], 6));
        var checkpointAfterFailure = ReadCheckpoint(strategy);
        _ = await strategy.CompactAsync([a, b, c, d, e], 6);
        var checkpointAfterRetry = ReadCheckpoint(strategy);

        // Assert
        Assert.Equal(3, summarizer.CallCount);
        Assert.Equal(checkpointBeforePromotion.CoveredCount, checkpointAfterFailure.CoveredCount);
        Assert.Equal(checkpointBeforePromotion.Fingerprint, checkpointAfterFailure.Fingerprint);
        Assert.Same(checkpointBeforePromotion.Summary, checkpointAfterFailure.Summary);
        Assert.Equal(3, checkpointAfterRetry.CoveredCount);
        Assert.Equal("summary-3", ReadText(checkpointAfterRetry.Summary!));
    }

    [Fact]
    public async Task CompactAsync_CancellationBeforeLlmCall_ThrowsAndLeavesCheckpointUntouched()
    {
        // Arrange
        var old = ContextMessage.FromText(MessageRole.User, "old");
        var keep = ContextMessage.FromText(MessageRole.Model, "keep");
        var summarizer = new TrackingSummarizer("unused");
        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(old, 4);
        tokenCounter.Set(keep, 4);

        var strategy = new LlmSummarizationStrategy(
            summarizer,
            tokenCounter,
            new LlmSummarizationOptions(windowSize: 1, minSummaryTokens: 1, maxSummaryTokens: 100));

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        await Assert.ThrowsAsync<OperationCanceledException>(() => strategy.CompactAsync([old, keep], 10, cts.Token));
        var checkpoint = ReadCheckpoint(strategy);

        // Assert
        Assert.Equal(0, summarizer.CallCount);
        Assert.Equal(0, checkpoint.CoveredCount);
        Assert.Equal(0L, checkpoint.Fingerprint);
        Assert.Null(checkpoint.Summary);
    }

    [Fact]
    public async Task CompactAsync_CancellationDuringPromotion_PreservesCheckpointFromPriorCall()
    {
        // Arrange
        var a = ContextMessage.FromText(MessageRole.User, "A");
        var b = ContextMessage.FromText(MessageRole.Model, "B");
        var c = ContextMessage.FromText(MessageRole.User, "C");
        var d = ContextMessage.FromText(MessageRole.Model, "D");
        var e = ContextMessage.FromText(MessageRole.User, "E");

        var summarizer = new TrackingSummarizer(async (invocation, cancellationToken) =>
        {
            if (invocation.CallNumber == 1)
            {
                return "summary-1";
            }

            await Task.Delay(Timeout.Infinite, cancellationToken);
            return "unreachable";
        });

        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(a, 2);
        tokenCounter.Set(b, 2);
        tokenCounter.Set(c, 2);
        tokenCounter.Set(d, 2);
        tokenCounter.Set(e, 2);
        tokenCounter.SetByText("summary-1", 1);

        var strategy = new LlmSummarizationStrategy(
            summarizer,
            tokenCounter,
            new LlmSummarizationOptions(windowSize: 2, minSummaryTokens: 1, maxSummaryTokens: 100));

        _ = await strategy.CompactAsync([a, b, c, d], 6);
        var checkpointBeforeCancellation = ReadCheckpoint(strategy);

        using var cts = new CancellationTokenSource();

        // Act
        var compactionTask = strategy.CompactAsync([a, b, c, d, e], 6, cts.Token);
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => compactionTask);
        var checkpointAfterCancellation = ReadCheckpoint(strategy);

        // Assert
        Assert.Equal(2, summarizer.CallCount);
        Assert.Equal(checkpointBeforeCancellation.CoveredCount, checkpointAfterCancellation.CoveredCount);
        Assert.Equal(checkpointBeforeCancellation.Fingerprint, checkpointAfterCancellation.Fingerprint);
        Assert.Same(checkpointBeforeCancellation.Summary, checkpointAfterCancellation.Summary);
    }

    [Fact]
    public async Task CompactAsync_SameBoundaryResumarization_UpdatesSyntheticSummaryOnly()
    {
        // Arrange
        var a = ContextMessage.FromText(MessageRole.User, "A");
        var b = ContextMessage.FromText(MessageRole.Model, "B");
        var c = ContextMessage.FromText(MessageRole.User, "C");
        var d = ContextMessage.FromText(MessageRole.Model, "D");

        var summarizer = new TrackingSummarizer(invocation => Task.FromResult($"summary-{invocation.CallNumber}"));
        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(a, 2);
        tokenCounter.Set(b, 2);
        tokenCounter.Set(c, 2);
        tokenCounter.Set(d, 2);
        tokenCounter.SetByText("summary-1", 1);
        tokenCounter.SetByText("summary-2", 1);

        var strategy = new LlmSummarizationStrategy(
            summarizer,
            tokenCounter,
            new LlmSummarizationOptions(windowSize: 2, minSummaryTokens: 1, maxSummaryTokens: 100));

        // Act
        _ = await strategy.CompactAsync([a, b, c, d], 10);
        var before = ReadCheckpoint(strategy);
        var compacted = await strategy.CompactAsync([a, b, c, d], 4);
        var after = ReadCheckpoint(strategy);

        // Assert
        Assert.Equal(2, summarizer.CallCount);
        Assert.Equal([a, b], summarizer.Calls[1].Messages);
        Assert.Equal(before.CoveredCount, after.CoveredCount);
        Assert.Equal(before.Fingerprint, after.Fingerprint);
        Assert.NotSame(before.Summary, after.Summary);
        Assert.Equal("summary-2", ReadText(compacted.Messages[0]));
    }

    [Fact]
    public async Task CompactAsync_MessageWithNullContent_ComputesFingerprintWithoutThrowing()
    {
        // Arrange
        var summarize = ContextMessage.FromContent(MessageRole.User, new TextContent("seed") { Content = null! });
        var keep = ContextMessage.FromText(MessageRole.Model, "keep");

        var summarizer = new TrackingSummarizer("summary");
        var tokenCounter = new TrackingTokenCounter();
        tokenCounter.Set(summarize, 4);
        tokenCounter.Set(keep, 4);
        tokenCounter.SetByText("summary", 1);

        var strategy = new LlmSummarizationStrategy(
            summarizer,
            tokenCounter,
            new LlmSummarizationOptions(windowSize: 1, minSummaryTokens: 1, maxSummaryTokens: 100));

        // Act
        _ = await strategy.CompactAsync([summarize, keep], 10);
        var compacted = await strategy.CompactAsync([summarize, keep], 10);

        // Assert
        Assert.Equal(1, summarizer.CallCount);
        Assert.Equal("summary", ReadText(compacted.Messages[0]));
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

    private static (int CoveredCount, long Fingerprint, ContextMessage? Summary) ReadCheckpoint(LlmSummarizationStrategy strategy)
    {
        var strategyType = typeof(LlmSummarizationStrategy);
        var coveredCount = (int)strategyType
            .GetField("_checkpointCoveredCount", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(strategy)!;
        var fingerprint = (long)strategyType
            .GetField("_checkpointPrefixFingerprint", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(strategy)!;
        var summary = (ContextMessage?)strategyType
            .GetField("_checkpointSyntheticSummary", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(strategy);

        return (coveredCount, fingerprint, summary);
    }

    private static string ReadText(ContextMessage message)
    {
        return Assert.IsType<TextContent>(Assert.Single(message.Segments)).Content!;
    }

    private sealed class TrackingSummarizer : ILlmSummarizer
    {
        private readonly Func<SummarizerCall, CancellationToken, Task<string>> _handler;

        public TrackingSummarizer(string summary)
            : this(_ => Task.FromResult(summary))
        {
        }

        public TrackingSummarizer(Func<SummarizerCall, Task<string>> handler)
            : this((call, _) => handler(call))
        {
        }

        public TrackingSummarizer(Func<SummarizerCall, CancellationToken, Task<string>> handler)
        {
            this._handler = handler;
        }

        public int CallCount => this.Calls.Count;

        public List<SummarizerCall> Calls { get; } = [];

        public IReadOnlyList<ContextMessage> LastMessages => this.Calls.Count == 0 ? [] : this.Calls[^1].Messages;

        public int LastTargetTokens => this.Calls.Count == 0 ? 0 : this.Calls[^1].TargetTokens;

        public async Task<string> SummarizeAsync(
            IReadOnlyList<ContextMessage> messages,
            int targetTokens,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var call = new SummarizerCall(this.Calls.Count + 1, messages.ToArray(), targetTokens);
            this.Calls.Add(call);
            return await this._handler(call, cancellationToken);
        }
    }

    private sealed class TrackingTokenCounter : ITokenCounter
    {
        private readonly Dictionary<ContextMessage, int> _counts = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<string, int> _textCounts = new(StringComparer.Ordinal);

        public void Set(ContextMessage contextMessage, int count)
        {
            this._counts[contextMessage] = count;
        }

        public void SetByText(string text, int count)
        {
            this._textCounts[text] = count;
        }

        public int Count(ContextMessage contextMessage)
        {
            if (this._counts.TryGetValue(contextMessage, out var value))
            {
                return value;
            }

            if (contextMessage.Segments.Count == 1
                && contextMessage.Segments[0] is TextContent text
                && text.Content is not null
                && this._textCounts.TryGetValue(text.Content, out value))
            {
                return value;
            }

            return 1;
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

    private sealed record SummarizerCall(int CallNumber, IReadOnlyList<ContextMessage> Messages, int TargetTokens);
}
