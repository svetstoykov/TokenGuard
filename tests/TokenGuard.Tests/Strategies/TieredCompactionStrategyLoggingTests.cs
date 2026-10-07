using FluentAssertions;
using Microsoft.Extensions.Logging;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Options;
using TokenGuard.Core.Strategies;
using TokenGuard.Tests.Diagnostics;

namespace TokenGuard.Tests.Strategies;

public sealed class TieredCompactionStrategyLoggingTests
{
    private const int TieredResultSelected = 4000;
    private const string TieredCategory = "TokenGuard.Core.Strategies.TieredCompactionStrategy";

    [Fact]
    public async Task CompactAsync_WhenSlidingWindowFits_LogsSlidingWindowSufficient()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        var strategy = CreateStrategy(logs, ScriptedSummarizer.Returning("summary"));

        // Act
        await strategy.CompactAsync(CreateHistory(), availableTokens: 500);

        // Assert
        AssertSelection(logs, "SlidingWindow", "SlidingWindowSufficient", returnedTokens: 100, availableTokens: 500);
    }

    [Fact]
    public async Task CompactAsync_WhenOverBudgetWithoutSummarizer_LogsNoSummarizerConfigured()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        var strategy = CreateStrategy(logs, summarizer: null);

        // Act
        await strategy.CompactAsync(CreateHistory(), availableTokens: 60);

        // Assert
        AssertSelection(logs, "SlidingWindow", "NoSummarizerConfigured", returnedTokens: 100, availableTokens: 60);
    }

    [Fact]
    public async Task CompactAsync_WhenSummarizationFits_LogsSummarizationSucceeded()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        var strategy = CreateStrategy(logs, ScriptedSummarizer.Returning(new string('s', 10)));

        // Act
        await strategy.CompactAsync(CreateHistory(), availableTokens: 60);

        // Assert
        AssertSelection(logs, "Summarization", "SummarizationSucceeded", returnedTokens: 50, availableTokens: 60);
    }

    [Fact]
    public async Task CompactAsync_WhenSummarizationOvershoots_LogsSummarizationOvershot()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        var strategy = CreateStrategy(logs, ScriptedSummarizer.Returning(new string('s', 30)));

        // Act
        await strategy.CompactAsync(CreateHistory(), availableTokens: 60);

        // Assert
        AssertSelection(logs, "SlidingWindow", "SummarizationOvershot", returnedTokens: 100, availableTokens: 60);
    }

    [Fact]
    public async Task CompactAsync_WhenSummarizationThrows_LogsSummarizationThrewWithoutTheException()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        var strategy = CreateStrategy(logs, ScriptedSummarizer.Throwing(new TimeoutException("provider timed out")));

        // Act
        var result = await strategy.CompactAsync(CreateHistory(), availableTokens: 60);

        // Assert
        result.SummarizationError.Should().BeOfType<TimeoutException>();
        AssertSelection(logs, "SlidingWindow", "SummarizationThrew", returnedTokens: 100, availableTokens: 60);
        logs.Records.Should().OnlyContain(record => record.Exception == null);
    }

    [Fact]
    public async Task CompactAsync_WhenSummarizationThrewForTheSameHistoryBefore_ReturnsTheSlidingWindowResultWithoutANewError()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        var summarizer = ScriptedSummarizer.Throwing(new TimeoutException("provider timed out"));
        var strategy = CreateStrategy(logs, summarizer);
        var history = CreateHistory();
        await strategy.CompactAsync(history, availableTokens: 60);

        // Act
        var result = await strategy.CompactAsync(history, availableTokens: 60);

        // Assert
        summarizer.Calls.Should().Be(1);
        result.SummarizationError.Should().BeNull();
        result.TokensAfter.Should().Be(100);
        result.Messages.Should().Equal(history);
    }

    private static void AssertSelection(CapturingLoggerFactory logs, string result, string reason, int returnedTokens, int availableTokens)
    {
        var record = logs.WithEventId(TieredResultSelected).Should().ContainSingle().Subject;
        record.Level.Should().Be(LogLevel.Debug);
        record.Category.Should().Be(TieredCategory);
        record.Property("Result").Should().Be(result);
        record.Property("Reason").Should().Be(reason);
        record.Property("SlidingWindowTokens").Should().Be(100);
        record.Property("ReturnedTokens").Should().Be(returnedTokens);
        record.Property("AvailableTokens").Should().Be(availableTokens);
    }

    private static TieredCompactionStrategy CreateStrategy(CapturingLoggerFactory logs, ILlmSummarizer? summarizer)
    {
        var counter = new TextLengthTokenCounter();
        var summarization = summarizer is null
            ? null
            : new LlmSummarizationStrategy(
                summarizer, counter, new LlmSummarizationOptions(windowSize: 2, minSummaryTokens: 5, maxSummaryTokens: 50),
                logs.CreateLogger<LlmSummarizationStrategy>());

        return new TieredCompactionStrategy(
            counter, new SlidingWindowOptions(windowSize: 1, protectedWindowFraction: 0.20), summarization,
            logs.CreateLogger<TieredCompactionStrategy>(), logs.CreateLogger<SlidingWindowStrategy>());
    }

    private static ContextMessage[] CreateHistory() =>
        Enumerable.Range(0, 5)
            .Select(index => ContextMessage.FromText(index % 2 == 0 ? MessageRole.User : MessageRole.Model, new string('a', 20)))
            .ToArray();
}
