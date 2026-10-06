using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;
using TokenGuard.Core.Options;
using TokenGuard.Core.Strategies;
using TokenGuard.Tests.Diagnostics;

namespace TokenGuard.Tests.Strategies;

/// <summary>
///     Verifies that a strategy returns the same <see cref="CompactionResult" /> with trace logging as without a logger.
/// </summary>
public sealed class StrategyLoggingEquivalenceTests
{
    public static TheoryData<int> AvailableTokenBudgets => [20, 45, 60, 90, 150, 10_000];

    [Theory]
    [MemberData(nameof(AvailableTokenBudgets))]
    public async Task SlidingWindowStrategy_WithTraceLogging_ReturnsTheSameResultAsWithoutLogging(int availableTokens)
    {
        // Arrange
        var options = new SlidingWindowOptions(windowSize: 1, protectedWindowFraction: 0.20);
        var silent = new SlidingWindowStrategy(new TextLengthTokenCounter(), options);
        var logged = new SlidingWindowStrategy(
            new TextLengthTokenCounter(), options, new CapturingLoggerFactory().CreateLogger<SlidingWindowStrategy>());
        var history = CreateHistory();

        // Act
        var expected = await silent.CompactAsync(history, availableTokens);
        var actual = await logged.CompactAsync(history, availableTokens);

        // Assert
        AssertEquivalent(actual, expected);
    }

    [Theory]
    [MemberData(nameof(AvailableTokenBudgets))]
    public async Task LlmSummarizationStrategy_WithTraceLogging_ReturnsTheSameResultsAsWithoutLogging(int availableTokens)
    {
        // Arrange
        var silent = CreateSummarization(NullLoggerFactory.Instance);
        var logged = CreateSummarization(new CapturingLoggerFactory());
        var history = CreateHistory();

        // Act
        var expected = await CompactTwiceAsync(silent, history, availableTokens);
        var actual = await CompactTwiceAsync(logged, history, availableTokens);

        // Assert
        AssertEquivalent(actual.First, expected.First);
        AssertEquivalent(actual.Second, expected.Second);
    }

    [Theory]
    [MemberData(nameof(AvailableTokenBudgets))]
    public async Task TieredCompactionStrategy_WithTraceLogging_ReturnsTheSameResultsAsWithoutLogging(int availableTokens)
    {
        // Arrange
        var silent = CreateTiered(NullLoggerFactory.Instance);
        var logged = CreateTiered(new CapturingLoggerFactory());
        var history = CreateHistory();

        // Act
        var expected = await CompactTwiceAsync(silent, history, availableTokens);
        var actual = await CompactTwiceAsync(logged, history, availableTokens);

        // Assert
        AssertEquivalent(actual.First, expected.First);
        AssertEquivalent(actual.Second, expected.Second);
    }

    private static async Task<(CompactionResult First, CompactionResult Second)> CompactTwiceAsync(
        ICompactionStrategy strategy, ContextMessage[] history, int availableTokens)
    {
        var first = await strategy.CompactAsync(history.Take(6).ToArray(), availableTokens);
        var second = await strategy.CompactAsync(history, availableTokens);
        return (first, second);
    }

    private static LlmSummarizationStrategy CreateSummarization(ILoggerFactory loggerFactory) =>
        new(
            ScriptedSummarizer.Returning(new string('s', 10)), new TextLengthTokenCounter(),
            new LlmSummarizationOptions(windowSize: 2, minSummaryTokens: 5, maxSummaryTokens: 50),
            loggerFactory.CreateLogger<LlmSummarizationStrategy>());

    private static TieredCompactionStrategy CreateTiered(ILoggerFactory loggerFactory) =>
        new(
            new TextLengthTokenCounter(), new SlidingWindowOptions(windowSize: 1, protectedWindowFraction: 0.20),
            CreateSummarization(loggerFactory), loggerFactory.CreateLogger<TieredCompactionStrategy>(),
            loggerFactory.CreateLogger<SlidingWindowStrategy>());

    private static ContextMessage[] CreateHistory() =>
    [
        ContextMessage.FromText(MessageRole.User, new string('a', 20)),
        ContextMessage.FromContent(MessageRole.Model, new ToolUseContent("call_1", "search", "{\"query\":\"q\"}")),
        ContextMessage.FromContent(MessageRole.Tool, new ToolResultContent("call_1", "search", new string('r', 50))),
        ContextMessage.FromText(MessageRole.Model, new string('b', 20)),
        ContextMessage.FromText(MessageRole.User, new string('c', 20)),
        ContextMessage.FromText(MessageRole.Model, new string('d', 20)),
        ContextMessage.FromText(MessageRole.User, new string('e', 20)),
        ContextMessage.FromText(MessageRole.Model, new string('f', 20)),
    ];

    private static void AssertEquivalent(CompactionResult actual, CompactionResult expected)
    {
        actual.TokensBefore.Should().Be(expected.TokensBefore);
        actual.TokensAfter.Should().Be(expected.TokensAfter);
        actual.MessagesAffected.Should().Be(expected.MessagesAffected);
        actual.StrategyName.Should().Be(expected.StrategyName);
        // A summary message is stamped with its creation time, which differs between the two runs.
        actual.Messages.Should().BeEquivalentTo(expected.Messages, options => options.WithStrictOrdering().Excluding(message => message.Timestamp));
    }
}
