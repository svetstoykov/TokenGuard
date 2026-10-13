using FluentAssertions;
using TokenGuard.Core;
using TokenGuard.Core.Configuration;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;
using TokenGuard.Core.Options;

namespace TokenGuard.Tests.Core;

public sealed class ConversationContextHistoryTests
{
    [Fact]
    public async Task PrepareAsync_WhenNothingWasRecorded_ReturnsAnEmptyReadyResult()
    {
        // Arrange
        using var context = new ConversationContextFactory(ConversationConfigBuilder.Default()).Create();

        // Act
        var result = await context.PrepareAsync();

        // Assert
        result.Outcome.Should().Be(PrepareOutcome.Ready);
        result.Messages.Should().BeEmpty();
        result.TokensBeforeCompaction.Should().Be(0);
        result.TokensAfterCompaction.Should().Be(0);
    }

    [Fact]
    public async Task PrepareAsync_WhenThePreparedViewIsMasked_LeavesTheRecordedHistoryUnchanged()
    {
        // Arrange
        var configuration = new ConversationConfigBuilder()
            .WithMaxTokens(1_200)
            .WithSlidingWindowOptions(new SlidingWindowOptions(windowSize: 2))
            .Build();
        using var context = new ConversationContextFactory(configuration).Create();
        context.SetSystemPrompt("You are a careful coding assistant.");
        context.AddUserMessage("Read the three log files and tell me which service restarted.");

        for (var call = 1; call <= 3; call++)
        {
            context.RecordModelResponse([new ToolUseContent($"call_{call}", "read_log", $$"""{"file":"service-{{call}}.log"}""")]);
            context.RecordToolResult($"call_{call}", "read_log", LogLines(call));
        }

        var recorded = context.History.ToArray();
        var recordedText = recorded.Select(Describe).ToArray();

        // Act
        var result = await context.PrepareAsync();

        // Assert
        result.Outcome.Should().Be(PrepareOutcome.Compacted);
        result.Messages.Should().Contain(message => message.State == CompactionState.Masked);
        context.History.Should().Equal(recorded, (actual, expected) => ReferenceEquals(actual, expected));
        context.History.Select(Describe).Should().Equal(recordedText);
        context.History.Should().OnlyContain(message => message.State == CompactionState.Original);
    }

    private static string LogLines(int service) =>
        string.Join(
            '\n',
            Enumerable.Range(1, 12).Select(line => $"2026-05-12T03:{line:00}:00Z service-{service} INFO heartbeat ok, queue depth {line * 3}"));

    private static string Describe(ContextMessage message) =>
        $"{message.Role}|{message.State}|{message.IsPinned}|{string.Join("|", message.Segments.Select(segment => segment.Content))}";
}
