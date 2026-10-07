using FluentAssertions;
using TokenGuard.Core;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Configuration;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;
using TokenGuard.Core.Options;
using TokenGuard.Extensions.OpenAI;
using TokenGuard.Tests.Diagnostics;

namespace TokenGuard.Tests.Core;

public sealed class ConversationContextPinnedPlacementTests
{
    private const string Pin = "PIN";
    private const string Summary = "SUMMARY";

    public static TheoryData<int> Seeds => new(Enumerable.Range(1, 40));

    [Fact]
    public async Task PrepareAsync_WhenSummaryReplacesMessagesBeforeMidConversationPin_PlacesPinAfterSummaryAndOutsideToolExchange()
    {
        // Arrange
        var builder = new ConversationConfigBuilder().WithMaxTokens(25_000);
        builder.SetLlmSummarizer(() => ScriptedSummarizer.Returning(Summary), "Test");
        using var context = new ConversationContextFactory(builder.Build()).Create();

        context.SetSystemPrompt("system");
        context.AddUserMessage(Prose(6_000));
        await context.PrepareAsync();
        context.RecordModelResponse([new TextContent(Prose(6_000))]);
        context.AddUserMessage(Prose(5_000));
        await context.PrepareAsync();
        context.RecordModelResponse([new TextContent("model-2")]);
        context.AddUserMessage("user-3");
        await context.PrepareAsync();
        context.RecordModelResponse([new TextContent("model-3")]);
        context.AddPinnedMessage(MessageRole.User, Pin);
        context.AddUserMessage("user-4");
        await context.PrepareAsync();
        context.RecordModelResponse([new ToolUseContent("call_1", "search", "{}")]);
        context.RecordToolResult("call_1", "search", Prose(9_000));

        // Act
        var prepared = (await context.PrepareAsync()).Messages;

        // Assert
        prepared.Select(Describe).Should().Equal(
            "System:system", "Summary", "User:user-3", "Model:model-3", "User:PIN", "User:user-4", "Model:call_1", "Tool:call_1");
        prepared.Invoking(messages => messages.ForOpenAI()).Should().NotThrow();
    }

    [Fact]
    public async Task PrepareAsync_WhenSummaryReplacesMessagesOnBothSidesOfPin_PlacesPinDirectlyAfterSummary()
    {
        // Arrange
        using var context = CreateContext(SummarizeOldest(3));
        context.AddUserMessage(Text("user-1"));
        context.AddPinnedMessage(MessageRole.User, Pin);
        context.RecordModelResponse([new TextContent(Text("model-1"))]);
        context.AddUserMessage(Text("user-2"));
        context.RecordModelResponse([new TextContent(Text("model-2"))]);

        // Act
        var prepared = (await context.PrepareAsync()).Messages;

        // Assert
        prepared.Select(Describe).Should().Equal("Summary", "User:PIN", "Model:model-2");
    }

    [Fact]
    public async Task PrepareAsync_WhenStrategyDropsMessagesBeforePin_PlacesPinBeforeFirstSurvivingLaterMessage()
    {
        // Arrange
        using var context = CreateContext(StubCompactionStrategy.KeepNewest(2));
        context.AddUserMessage(Text("user-1"));
        context.RecordModelResponse([new TextContent(Text("model-1"))]);
        context.AddPinnedMessage(MessageRole.User, Pin);
        context.AddUserMessage(Text("user-2"));
        context.RecordModelResponse([new TextContent(Text("model-2"))]);

        // Act
        var prepared = (await context.PrepareAsync()).Messages;

        // Assert
        prepared.Select(Describe).Should().Equal("User:PIN", "User:user-2", "Model:model-2");
    }

    [Fact]
    public async Task PrepareAsync_WhenPinWasRecordedBeforeAnyOtherMessage_KeepsSystemPromptFirstAndPinBeforeSummary()
    {
        // Arrange
        using var context = CreateContext(SummarizeOldest(2));
        context.AddPinnedMessage(MessageRole.User, Pin);
        context.SetSystemPrompt("system");
        context.AddUserMessage(Text("user-1"));
        context.RecordModelResponse([new TextContent(Text("model-1"))]);
        context.AddUserMessage(Text("user-2"));

        // Act
        var prepared = (await context.PrepareAsync()).Messages;

        // Assert
        prepared.Select(Describe).Should().Equal("System:system", "User:PIN", "Summary", "User:user-2");
    }

    [Fact]
    public async Task PrepareAsync_WhenCompactedViewWouldPutPinBetweenToolCallAndResult_PlacesPinBeforeTheModelMessage()
    {
        // Arrange
        using var context = CreateContext(SummarizeOldest(2));
        context.AddUserMessage(Text("user-1"));
        context.RecordModelResponse([new TextContent(Text("model-1"))]);
        context.AddUserMessage(Text("user-2"));
        context.RecordModelResponse([new ToolUseContent("call_1", "search", "{}"), new ToolUseContent("call_2", "search", "{}")]);
        context.RecordToolResult("call_1", "search", Text("result-1"));
        context.AddPinnedMessage(MessageRole.User, Pin);
        context.RecordToolResult("call_2", "search", Text("result-2"));

        // Act
        var prepared = (await context.PrepareAsync()).Messages;

        // Assert
        prepared.Select(Describe).Should().Equal("Summary", "User:user-2", "User:PIN", "Model:call_1,call_2", "Tool:call_1", "Tool:call_2");
        prepared.Invoking(messages => messages.ForOpenAI()).Should().NotThrow();
    }

    [Fact]
    public async Task PrepareAsync_WhenSummaryIsFollowedByTheUserMessageThatOpenedTheToolLoop_PlacesALaterPinAfterThatMessage()
    {
        // Arrange
        var strategy = new StubCompactionStrategy((messages, _) => new CompactionResult(
            [ContextMessage.FromText(MessageRole.Model, Summary) with { State = CompactionState.Summarized }, messages[0], .. messages.Skip(3)],
            messages.Sum(message => message.TokenCount ?? 0), 0, 2, StubCompactionStrategy.Name));
        using var context = CreateContext(strategy);
        context.AddUserMessage(Text("user-1"));
        context.AddPinnedMessage(MessageRole.User, Pin);
        context.RecordModelResponse([new ToolUseContent("call_1", "search", "{}")]);
        context.RecordToolResult("call_1", "search", Text("result-1"));
        context.RecordModelResponse([new ToolUseContent("call_2", "search", "{}")]);
        context.RecordToolResult("call_2", "search", Text("result-2"));

        // Act
        var prepared = (await context.PrepareAsync()).Messages;

        // Assert
        prepared.Select(Describe).Should().Equal("Summary", "User:user-1", "User:PIN", "Model:call_2", "Tool:call_2");
    }

    [Fact]
    public async Task PrepareAsync_WhenBelowTriggerAndPinWasRecordedBetweenToolCallAndResult_PlacesPinBeforeTheModelMessage()
    {
        // Arrange
        using var context = CreateContext(StubCompactionStrategy.Unchanged());
        context.AddUserMessage("user-1");
        context.RecordModelResponse([new ToolUseContent("call_1", "search", "{}")]);
        context.AddPinnedMessage(MessageRole.User, Pin);
        context.RecordToolResult("call_1", "search", "result-1");

        // Act
        var result = await context.PrepareAsync();

        // Assert
        result.Outcome.Should().Be(PrepareOutcome.Ready);
        result.Messages.Select(Describe).Should().Equal("User:user-1", "User:PIN", "Model:call_1", "Tool:call_1");
        result.Messages.Invoking(messages => messages.ForOpenAI()).Should().NotThrow();
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public async Task PrepareAsync_WhenPinsAreAddedAtRandomPointsWithSummarization_KeepsHistoryOrderAndConvertsForOpenAI(int seed)
    {
        // Arrange
        var random = new Random(seed);
        var builder = new ConversationConfigBuilder().WithMaxTokens(2_000).WithSlidingWindowOptions(new SlidingWindowOptions(windowSize: 2));
        var summarization = new LlmSummarizationOptions(windowSize: 2, minSummaryTokens: 10, maxSummaryTokens: 100);
        builder.SetLlmSummarizer(() => ScriptedSummarizer.Returning(Summary), "Test", summarization);
        using var context = new ConversationContextFactory(builder.Build()).Create();
        var violations = new List<string>();
        var pins = 0;
        var calls = 0;

        void MaybePin()
        {
            if (random.Next(4) == 0)
                context.AddPinnedMessage(random.Next(2) == 0 ? MessageRole.User : MessageRole.Model, $"{Pin}-{++pins}");
        }

        async Task PrepareAndCheckAsync()
        {
            var prepared = (await context.PrepareAsync()).Messages;
            var historyIndexes = prepared.Select(message => HistoryIndexOf(context.History, message)).Where(index => index >= 0).ToArray();

            if (!historyIndexes.SequenceEqual(historyIndexes.Order()))
                violations.Add($"reordered view: {string.Join(" | ", prepared.Select(Describe))}");

            try
            {
                prepared.ForOpenAI();
            }
            catch (InvalidOperationException exception)
            {
                violations.Add($"{exception.Message} View: {string.Join(" | ", prepared.Select(Describe))}");
            }
        }

        // Act
        context.SetSystemPrompt("system");
        for (var turn = 1; turn <= 12; turn++)
        {
            MaybePin();
            context.AddUserMessage(Prose(random.Next(20, 400)));
            MaybePin();
            await PrepareAndCheckAsync();

            for (var toolRounds = random.Next(3); toolRounds > 0; toolRounds--)
            {
                var callIds = Enumerable.Range(0, random.Next(1, 3)).Select(_ => $"call_{++calls}").ToArray();
                context.RecordModelResponse(callIds.Select(id => new ToolUseContent(id, "search", "{}")));
                foreach (var callId in callIds)
                    context.RecordToolResult(callId, "search", Prose(random.Next(20, 600)));

                MaybePin();
                await PrepareAndCheckAsync();
            }

            context.RecordModelResponse([new TextContent(Prose(random.Next(20, 400)))]);
        }

        // Assert
        pins.Should().BeGreaterThan(0, "the seed must add at least one pinned message");
        violations.Should().BeEmpty();
    }

    private static ConversationContext CreateContext(ICompactionStrategy strategy) =>
        new(new ContextBudget(100, compactionThreshold: 0.5), new TextLengthTokenCounter(), strategy);

    /// <summary>
    ///     Creates a strategy that replaces the oldest messages with one summary message, as LLM summarization does.
    /// </summary>
    /// <param name="count">The number of oldest messages the summary replaces.</param>
    /// <returns>The strategy.</returns>
    private static StubCompactionStrategy SummarizeOldest(int count) =>
        new((messages, _) => new CompactionResult(
            [ContextMessage.FromText(MessageRole.Model, Summary) with { State = CompactionState.Summarized }, .. messages.Skip(count)],
            messages.Sum(message => message.TokenCount ?? 0), 0, count, StubCompactionStrategy.Name));

    /// <summary>
    ///     Finds the history message a prepared message stands for.
    /// </summary>
    /// <param name="history">The recorded history.</param>
    /// <param name="message">A message from a prepared view.</param>
    /// <returns>The history index, or <c>-1</c> for a summary, which stands for several messages.</returns>
    private static int HistoryIndexOf(IReadOnlyList<ContextMessage> history, ContextMessage message)
    {
        if (message.State == CompactionState.Summarized)
            return -1;

        // A masked tool result is a copy, so it is matched to its original by tool call ID.
        var toolCallId = message.Segments.OfType<ToolResultContent>().FirstOrDefault()?.ToolCallId;

        for (var i = 0; i < history.Count; i++)
        {
            var matches = toolCallId is null
                ? ReferenceEquals(history[i], message)
                : history[i].Segments.OfType<ToolResultContent>().Any(result => result.ToolCallId == toolCallId);

            if (matches)
                return i;
        }

        throw new InvalidOperationException($"Prepared message '{Describe(message)}' is not in the history.");
    }

    private static string Describe(ContextMessage message)
    {
        if (message.State == CompactionState.Summarized)
            return "Summary";

        var label = message.Segments[0] switch
        {
            ToolUseContent => string.Join(",", message.Segments.OfType<ToolUseContent>().Select(toolUse => toolUse.ToolCallId)),
            ToolResultContent toolResult => toolResult.ToolCallId,
            var segment => segment.Content.Length > 20 ? $"{segment.Content.Length} characters" : segment.Content.TrimEnd('.'),
        };

        return $"{message.Role}:{label}";
    }

    private static string Text(string label) => label.PadRight(20, '.');

    private static string Prose(int tokens) => string.Join(' ', Enumerable.Repeat("word", tokens));
}
