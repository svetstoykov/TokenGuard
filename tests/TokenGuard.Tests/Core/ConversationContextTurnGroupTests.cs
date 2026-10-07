using FluentAssertions;
using TokenGuard.Core;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Configuration;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;
using TokenGuard.Extensions.OpenAI;
using TokenGuard.Tests.Diagnostics;

namespace TokenGuard.Tests.Core;

public sealed class ConversationContextTurnGroupTests
{
    private const int Pairs = 100;
    private const string FinalQuestion = "final question";

    [Fact]
    public async Task PrepareAsync_WhenHistoryWasRecordedWithoutPrepareCalls_KeepsTheNewestTurnGroupsThatFit()
    {
        // Arrange
        using var context = CreateDefaultContext(maxTokens: 2_000);
        await RecordPairsAndFinalQuestionAsync(context, prepareBeforeEachReply: false);
        var history = context.History.ToArray();
        var pairTokens = history[0].TokenCount!.Value + history[1].TokenCount!.Value;

        // Act
        var result = await context.PrepareAsync();

        // Assert
        result.Outcome.Should().Be(PrepareOutcome.Compacted);
        result.Messages.Should().Equal(history[^result.Messages.Count..], "the view is the newest part of the history");
        result.Messages[0].Role.Should().Be(MessageRole.User, "a view starts on the user message that opens a turn group");
        result.Messages.Count.Should().BeGreaterThan(1);
        result.TokensAfterCompaction.Should().BeInRange(2_000 - pairTokens + 1, 2_000, "one more turn group would exceed the budget");
    }

    [Fact]
    public async Task PrepareAsync_WhenTheSameHistoryWasRecordedWithPrepareCalls_ReturnsTheSameView()
    {
        // Arrange
        using var restored = CreateDefaultContext(maxTokens: 2_000);
        using var live = CreateDefaultContext(maxTokens: 2_000);
        await RecordPairsAndFinalQuestionAsync(restored, prepareBeforeEachReply: false);
        await RecordPairsAndFinalQuestionAsync(live, prepareBeforeEachReply: true);

        // Act
        var restoredResult = await restored.PrepareAsync();
        var liveResult = await live.PrepareAsync();

        // Assert
        liveResult.Messages.Select(Describe).Should().Equal(restoredResult.Messages.Select(Describe));
        liveResult.TokensAfterCompaction.Should().Be(restoredResult.TokensAfterCompaction);
        liveResult.Outcome.Should().Be(restoredResult.Outcome);
    }

    [Fact]
    public async Task PrepareAsync_WhenAnOlderTurnGroupHoldsAToolExchange_DropsTheWholeGroupTogether()
    {
        // Arrange
        using var context = CreateContext();
        context.AddUserMessage(Text("user-1", 20));
        context.RecordModelResponse([new ToolUseContent("call_1", "search", Text("{}", 10))]);
        context.RecordToolResult("call_1", "search", Text("result", 20));
        context.RecordModelResponse([new TextContent(Text("model-1", 20))]);
        context.AddUserMessage(Text("user-2", 20));
        context.RecordModelResponse([new TextContent(Text("model-2", 20))]);
        context.AddUserMessage(Text("user-3", 20));

        // Act
        var result = await context.PrepareAsync();

        // Assert
        result.Messages.Select(Describe).Should().Equal("User:user-2", "Model:model-2", "User:user-3");
        result.MessagesDropped.Should().Be(4);
    }

    [Fact]
    public async Task PrepareAsync_WhenTheNewestTurnGroupIsALongToolLoop_DropsItsOldestToolExchangesAndKeepsEachCallWithItsResult()
    {
        // Arrange
        using var context = CreateContext();
        context.AddUserMessage(Text("user-1", 20));
        for (var call = 1; call <= 3; call++)
        {
            context.RecordModelResponse([new ToolUseContent($"call_{call}", "search", Text("{}", 10))]);
            context.RecordToolResult($"call_{call}", "search", Text("result", 30));
        }

        // Act
        var result = await context.PrepareAsync();

        // Assert
        result.Messages.Where(message => message.Role != MessageRole.User).Select(Describe)
            .Should().Equal("Model:call_2", "Tool:call_2", "Model:call_3", "Tool:call_3");
        result.Messages.Invoking(messages => messages.ForOpenAI()).Should().NotThrow();
    }

    [Fact]
    public async Task PrepareAsync_WhenAPinnedUserMessageSitsInsideATurnGroup_DoesNotStartANewGroupAtThePin()
    {
        // Arrange
        using var context = CreateContext();
        context.AddUserMessage(Text("user-1", 40));
        context.RecordModelResponse([new ToolUseContent("call_1", "search", Text("{}", 10))]);
        context.AddPinnedMessage(MessageRole.User, Text("pin", 10));
        context.RecordToolResult("call_1", "search", Text("result", 20));
        context.RecordModelResponse([new TextContent(Text("model-1", 10))]);
        context.AddUserMessage(Text("user-2", 30));

        // Act
        var result = await context.PrepareAsync();

        // Assert
        result.Messages.Select(Describe).Should().Equal("User:pin", "User:user-2");
    }

    [Fact]
    public async Task PrepareAsync_WhenMessagesPrecedeTheFirstUserMessage_TreatsThemAsTheFirstTurnGroup()
    {
        // Arrange
        using var context = CreateContext();
        context.RecordModelResponse([new TextContent(Text("greeting", 40))]);
        context.AddUserMessage(Text("user-1", 30));
        context.RecordModelResponse([new TextContent(Text("model-1", 20))]);
        context.AddUserMessage(Text("user-2", 30));

        // Act
        var result = await context.PrepareAsync();

        // Assert
        result.Messages.Select(Describe).Should().Equal("User:user-1", "Model:model-1", "User:user-2");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrepareAsync_WhenSummarizing_KeepsTheSameTailWithAndWithoutEarlierPrepareCalls(bool prepareBeforeEachReply)
    {
        // Arrange
        var builder = new ConversationConfigBuilder().WithMaxTokens(25_000);
        builder.SetLlmSummarizer(() => ScriptedSummarizer.Returning("summary"), "Test");
        using var context = new ConversationContextFactory(builder.Build()).Create();
        IReadOnlyList<ContentSegment>[] replies =
        [
            [new TextContent(Prose(6_000))],
            [new TextContent("model-2")],
            [new TextContent("model-3")],
            [new ToolUseContent("call_1", "search", "{}")],
        ];
        string[] questions = [Prose(6_000), Prose(5_000), "user-3", "user-4"];

        for (var turn = 0; turn < questions.Length; turn++)
        {
            context.AddUserMessage(questions[turn]);
            if (prepareBeforeEachReply)
                await context.PrepareAsync();

            context.RecordModelResponse(replies[turn]);
        }

        context.RecordToolResult("call_1", "search", Prose(9_000));

        // Act
        var result = await context.PrepareAsync();

        // Assert
        result.Messages.Select(Describe).Should().Equal("Summary", "User:user-3", "Model:model-3", "User:user-4", "Model:call_1", "Tool:call_1");
    }

    private static IConversationContext CreateDefaultContext(int maxTokens) =>
        new ConversationContextFactory(new ConversationConfigBuilder().WithMaxTokens(maxTokens).Build()).Create();

    private static ConversationContext CreateContext() =>
        new(new ContextBudget(100, 0.5, emergencyThreshold: 1.0), new TextLengthTokenCounter(), StubCompactionStrategy.Unchanged());

    /// <summary>
    ///     Records the same user and model pairs and one final question, with or without a prepare call before each model reply.
    /// </summary>
    /// <param name="context">The context to record into.</param>
    /// <param name="prepareBeforeEachReply">Whether to call <c>PrepareAsync</c> before each model reply, as a live loop does.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private static async Task RecordPairsAndFinalQuestionAsync(IConversationContext context, bool prepareBeforeEachReply)
    {
        for (var pair = 1; pair <= Pairs; pair++)
        {
            context.AddUserMessage($"user-{pair:000} {Prose(20)}");
            if (prepareBeforeEachReply)
                await context.PrepareAsync();

            context.RecordModelResponse([new TextContent($"model-{pair:000} {Prose(20)}")]);
        }

        context.AddUserMessage(FinalQuestion);
    }

    private static string Describe(ContextMessage message)
    {
        if (message.State == CompactionState.Summarized)
            return "Summary";

        var label = message.Segments[0] switch
        {
            ToolUseContent toolUse => toolUse.ToolCallId,
            ToolResultContent toolResult => toolResult.ToolCallId,
            var segment => segment.Content.Split(' ', '.')[0],
        };

        return $"{message.Role}:{label}";
    }

    private static string Text(string label, int length) => label.PadRight(length, '.');

    private static string Prose(int words) => string.Join(' ', Enumerable.Repeat("word", words));
}
