using FluentAssertions;
using Microsoft.Extensions.Logging;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;
using TokenGuard.Core.Options;
using TokenGuard.Core.Strategies;
using TokenGuard.Tests.Diagnostics;

namespace TokenGuard.Tests.Strategies;

public sealed class SlidingWindowStrategyLoggingTests
{
    private const int SlidingWindowApplied = 2000;
    private const int ToolResultMasked = 2001;

    [Fact]
    public async Task CompactAsync_WhenToolResultsAreMasked_LogsOneDebugRecordWithThePassTotals()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        var strategy = CreateStrategy(logs);
        var messages = CreateHistoryWithOneOldToolResult();

        // Act
        var result = await strategy.CompactAsync(messages, availableTokens: 20);

        // Assert
        var record = logs.WithEventId(SlidingWindowApplied).Should().ContainSingle().Subject;
        record.Level.Should().Be(LogLevel.Debug);
        record.Property("MessageCount").Should().Be(4);
        record.Property("AvailableTokens").Should().Be(20);
        record.Property("TokensBefore").Should().Be(result.TokensBefore);
        record.Property("TokensAfter").Should().Be(result.TokensAfter);
        record.Property("WindowSize").Should().Be(1);
        record.Property("ProtectedMessages").Should().Be(1);
        record.Property("ToolResultsMasked").Should().Be(1);
    }

    [Fact]
    public async Task CompactAsync_WhenToolResultsAreMasked_LogsOneTraceRecordPerMaskedToolResult()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        var strategy = CreateStrategy(logs);
        var messages = CreateHistoryWithOneOldToolResult();

        // Act
        var result = await strategy.CompactAsync(messages, availableTokens: 20);

        // Assert
        var record = logs.WithEventId(ToolResultMasked).Should().ContainSingle().Subject;
        record.Level.Should().Be(LogLevel.Trace);
        record.Property("MessageIndex").Should().Be(1);
        record.Property("ToolCallId").Should().Be("call_1");
        record.Property("ToolName").Should().Be("search");
        record.Property("TokensBefore").Should().Be(50);
        record.Property("TokensAfter").Should().Be(result.Messages[1].Segments[0].Content.Length);
    }

    [Fact]
    public async Task CompactAsync_WhenEveryMessageIsProtected_LogsOneDebugRecordWithNothingMasked()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        var strategy = CreateStrategy(logs);
        var messages = CreateHistoryWithOneOldToolResult();

        // Act
        await strategy.CompactAsync(messages, availableTokens: 10_000);

        // Assert
        var record = logs.Records.Should().ContainSingle().Subject;
        record.EventId.Id.Should().Be(SlidingWindowApplied);
        record.Property("ProtectedMessages").Should().Be(4);
        record.Property("ToolResultsMasked").Should().Be(0);
    }

    [Fact]
    public async Task CompactAsync_AtDebugLevel_LogsNoPerMessageRecords()
    {
        // Arrange
        var logs = new CapturingLoggerFactory(LogLevel.Debug);
        var strategy = CreateStrategy(logs);
        var messages = CreateHistoryWithOneOldToolResult();

        // Act
        await strategy.CompactAsync(messages, availableTokens: 20);

        // Assert
        logs.WithEventId(ToolResultMasked).Should().BeEmpty();
        logs.WithEventId(SlidingWindowApplied).Should().ContainSingle().Which.Property("ToolResultsMasked").Should().Be(1);
    }

    private static SlidingWindowStrategy CreateStrategy(CapturingLoggerFactory logs) =>
        new(
            new TextLengthTokenCounter(), new SlidingWindowOptions(windowSize: 1, protectedWindowFraction: 0.20),
            logs.CreateLogger<SlidingWindowStrategy>());

    private static ContextMessage[] CreateHistoryWithOneOldToolResult() =>
    [
        ContextMessage.FromContent(MessageRole.Model, new ToolUseContent("call_1", "search", "{\"query\":\"q\"}")),
        ContextMessage.FromContent(MessageRole.Tool, new ToolResultContent("call_1", "search", new string('r', 50))),
        ContextMessage.FromText(MessageRole.User, "aaaaa"),
        ContextMessage.FromText(MessageRole.Model, "bbbbb"),
    ];
}
