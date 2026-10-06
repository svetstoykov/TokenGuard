using FluentAssertions;
using Microsoft.Extensions.Logging;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Options;
using TokenGuard.Core.Strategies;
using TokenGuard.Tests.Diagnostics;

namespace TokenGuard.Tests.Strategies;

public sealed class LlmSummarizationStrategyLoggingTests
{
    private const int PathSelected = 3000;
    private const int SkippedNothingToSummarize = 3010;
    private const int PromotedSummaryOvershot = 3011;
    private const int RefreshedSummaryOvershot = 3012;
    private const int SkippedInsufficientBudget = 3013;
    private const int FirstSummaryOvershot = 3014;
    private const int CheckpointCreated = 3020;
    private const int CheckpointReused = 3021;
    private const int CheckpointCleared = 3022;

    private static readonly string ShortSummary = new('s', 10);
    private static readonly string LongSummary = new('s', 30);

    [Fact]
    public async Task CompactAsync_WithoutCheckpoint_LogsThePathWithTailAndTargetNumbers()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        var strategy = CreateStrategy(logs, ScriptedSummarizer.Returning(ShortSummary));

        // Act
        await strategy.CompactAsync(CreateHistory(5), availableTokens: 60);

        // Assert
        var record = logs.WithEventId(PathSelected).Should().ContainSingle().Subject;
        record.Level.Should().Be(LogLevel.Debug);
        record.Property("Path").Should().Be("WithoutCheckpoint");
        record.Property("TailFirstIndex").Should().Be(3);
        record.Property("TailMessages").Should().Be(2);
        record.Property("SummarizableMessages").Should().Be(3);
        record.Property("SummarizableTokens").Should().Be(60);
        record.Property("TargetTokens").Should().Be(20);
    }

    [Fact]
    public async Task CompactAsync_WhenFirstSummaryFits_LogsCheckpointCreated()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        var strategy = CreateStrategy(logs, ScriptedSummarizer.Returning(ShortSummary));

        // Act
        await strategy.CompactAsync(CreateHistory(5), availableTokens: 60);

        // Assert
        var record = logs.WithEventId(CheckpointCreated).Should().ContainSingle().Subject;
        record.Property("SummarizedMessages").Should().Be(3);
        record.Property("TokensAfter").Should().Be(50);
    }

    [Fact]
    public async Task CompactAsync_WhenCheckpointStillFits_LogsThePathAndCheckpointReused()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        var strategy = CreateStrategy(logs, ScriptedSummarizer.Returning(ShortSummary));
        var history = CreateHistory(5);
        await strategy.CompactAsync(history, availableTokens: 60);

        // Act
        await strategy.CompactAsync(history, availableTokens: 60);

        // Assert
        logs.WithEventId(PathSelected).Last().Property("Path").Should().Be("WithCheckpoint");
        var record = logs.WithEventId(CheckpointReused).Should().ContainSingle().Subject;
        record.Property("SummarizedMessages").Should().Be(3);
        record.Property("TokensAfter").Should().Be(50);
        record.Property("AvailableTokens").Should().Be(60);
    }

    [Fact]
    public async Task CompactAsync_WhenEveryMessageIsInTheProtectedTail_LogsTheSkipReason()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        var strategy = CreateStrategy(logs, ScriptedSummarizer.Returning(ShortSummary));

        // Act
        await strategy.CompactAsync(CreateHistory(2), availableTokens: 10);

        // Assert
        var record = logs.Records.Should().ContainSingle().Subject;
        record.EventId.Id.Should().Be(SkippedNothingToSummarize);
        record.Property("MessageCount").Should().Be(2);
        record.Property("WindowSize").Should().Be(2);
    }

    [Fact]
    public async Task CompactAsync_WhenRemainingBudgetIsBelowTheMinimum_LogsTheSkipReasonWithTheBudgetNumbers()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        var strategy = CreateStrategy(logs, ScriptedSummarizer.Returning(ShortSummary));
        var history = CreateHistory(5);

        // Act
        var result = await strategy.CompactAsync(history, availableTokens: 42);

        // Assert
        result.Messages.Should().BeSameAs(history);
        var record = logs.WithEventId(SkippedInsufficientBudget).Should().ContainSingle().Subject;
        record.Property("RemainingBudget").Should().Be(2);
        record.Property("MinSummaryTokens").Should().Be(5);
        record.Property("AvailableTokens").Should().Be(42);
        record.Property("TailTokens").Should().Be(40);
    }

    [Fact]
    public async Task CompactAsync_WhenFirstSummaryOvershoots_LogsTheOvershootNumbers()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        var strategy = CreateStrategy(logs, ScriptedSummarizer.Returning(LongSummary));
        var history = CreateHistory(5);

        // Act
        var result = await strategy.CompactAsync(history, availableTokens: 60);

        // Assert
        result.Messages.Should().BeSameAs(history);
        var record = logs.WithEventId(FirstSummaryOvershot).Should().ContainSingle().Subject;
        record.Property("TokensAfter").Should().Be(70);
        record.Property("AvailableTokens").Should().Be(60);
        record.Property("SummarizedMessages").Should().Be(3);
        logs.WithEventId(CheckpointCreated).Should().BeEmpty();
    }

    [Fact]
    public async Task CompactAsync_WhenPromotedSummaryOvershoots_LogsTheOvershootNumbers()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        var strategy = CreateStrategy(logs, new ScriptedSummarizer(call => call == 1 ? ShortSummary : LongSummary));
        var history = CreateHistory(7);
        await strategy.CompactAsync(history.Take(5).ToArray(), availableTokens: 60);

        // Act
        var result = await strategy.CompactAsync(history, availableTokens: 60);

        // Assert
        result.Messages.Should().BeSameAs(history);
        var record = logs.WithEventId(PromotedSummaryOvershot).Should().ContainSingle().Subject;
        record.Property("TokensAfter").Should().Be(70);
        record.Property("AvailableTokens").Should().Be(60);
        record.Property("SummarizedMessages").Should().Be(5);
    }

    [Fact]
    public async Task CompactAsync_WhenPromotedSummaryFits_LogsCheckpointCreatedForTheLargerPrefix()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        var strategy = CreateStrategy(logs, ScriptedSummarizer.Returning(ShortSummary));
        var history = CreateHistory(7);
        await strategy.CompactAsync(history.Take(5).ToArray(), availableTokens: 60);

        // Act
        await strategy.CompactAsync(history, availableTokens: 60);

        // Assert
        logs.WithEventId(CheckpointCreated).Should().HaveCount(2);
        logs.WithEventId(CheckpointCreated).Last().Property("SummarizedMessages").Should().Be(5);
    }

    [Fact]
    public async Task CompactAsync_WhenRefreshedSummaryOvershoots_LogsTheOvershootNumbers()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        var strategy = CreateStrategy(logs, ScriptedSummarizer.Returning(ShortSummary));
        var history = CreateHistory(5);
        await strategy.CompactAsync(history, availableTokens: 60);

        // Act
        var result = await strategy.CompactAsync(history, availableTokens: 45);

        // Assert
        result.Messages.Should().BeSameAs(history);
        var record = logs.WithEventId(RefreshedSummaryOvershot).Should().ContainSingle().Subject;
        record.Property("TokensAfter").Should().Be(50);
        record.Property("AvailableTokens").Should().Be(45);
        record.Property("SummarizedMessages").Should().Be(3);
    }

    [Fact]
    public async Task CompactAsync_WhenSummarizedPrefixChanged_LogsCheckpointClearedWithThatReason()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        var strategy = CreateStrategy(logs, ScriptedSummarizer.Returning(ShortSummary));
        await strategy.CompactAsync(CreateHistory(5), availableTokens: 60);

        // Act
        await strategy.CompactAsync(CreateHistory(5, fill: 'z'), availableTokens: 60);

        // Assert
        var record = logs.WithEventId(CheckpointCleared).Should().ContainSingle().Subject;
        record.Property("Reason").Should().Be("SummarizedPrefixChanged");
        record.Property("SummarizedMessages").Should().Be(3);
        record.Property("MessageCount").Should().Be(5);
    }

    [Fact]
    public async Task CompactAsync_WhenHistoryIsShorterThanTheCheckpoint_LogsCheckpointClearedWithThatReason()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        var strategy = CreateStrategy(logs, ScriptedSummarizer.Returning(ShortSummary));
        var history = CreateHistory(6);
        await strategy.CompactAsync(history, availableTokens: 60);

        // Act
        await strategy.CompactAsync(history.Take(3).ToArray(), availableTokens: 60);

        // Assert
        var record = logs.WithEventId(CheckpointCleared).Should().ContainSingle().Subject;
        record.Property("Reason").Should().Be("HistoryShorterThanCheckpoint");
        record.Property("SummarizedMessages").Should().Be(4);
        record.Property("MessageCount").Should().Be(3);
    }

    [Fact]
    public async Task CompactAsync_NeverWritesTheSummaryTextToAnyRecord()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        var strategy = CreateStrategy(logs, ScriptedSummarizer.Returning("SENTINEL-s"));
        var history = CreateHistory(5);

        // Act
        await strategy.CompactAsync(history, availableTokens: 60);
        await strategy.CompactAsync(history, availableTokens: 60);

        // Assert
        logs.Records.Should().NotBeEmpty();
        logs.Records.Should().OnlyContain(record =>
            !record.Message.Contains("SENTINEL") && record.State.All(pair => !$"{pair.Value}".Contains("SENTINEL")));
    }

    private static LlmSummarizationStrategy CreateStrategy(CapturingLoggerFactory logs, ILlmSummarizer summarizer) =>
        new(
            summarizer, new TextLengthTokenCounter(), new LlmSummarizationOptions(windowSize: 2, minSummaryTokens: 5, maxSummaryTokens: 50),
            logs.CreateLogger<LlmSummarizationStrategy>());

    private static ContextMessage[] CreateHistory(int count, char fill = 'a') =>
        Enumerable.Range(0, count)
            .Select(index => ContextMessage.FromText(index % 2 == 0 ? MessageRole.User : MessageRole.Model, new string(fill, 20)))
            .ToArray();
}
