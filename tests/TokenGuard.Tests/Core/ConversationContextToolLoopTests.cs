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

public sealed class ConversationContextToolLoopTests
{
    [Fact]
    public async Task PrepareAsync_WhenAToolResultPushesTheFirstToolLoopOverTheLimit_KeepsTheUserMessageAndDoesNotReportCompacted()
    {
        // Arrange
        using var context = CreateDefaultContext(maxTokens: 1_000);
        context.SetSystemPrompt("You are a helpful assistant.");
        context.AddUserMessage(Prose(300));
        await context.PrepareAsync();
        context.RecordModelResponse([new ToolUseContent("call_1", "search", "{}")]);
        context.RecordToolResult("call_1", "search", Prose(860));
        var history = context.History.ToArray();

        // Act
        var result = await context.PrepareAsync();

        // Assert
        history[1].TokenCount.Should().BeInRange(290, 330, "the user message is sized like the one in the reported session");
        history[3].TokenCount.Should().BeInRange(850, 900, "the tool result is sized like the one in the reported session");
        result.Messages.Should().Equal(history);
        result.Outcome.Should().Be(PrepareOutcome.CannotCompact);
    }

    [Fact]
    public async Task PrepareAsync_WhenALongToolLoopExceedsTheLimit_KeepsTheOpeningUserMessageAndDropsTheOldestToolExchanges()
    {
        // Arrange
        using var context = CreateContext();
        context.AddUserMessage(Text("user-1", 20));
        context.RecordModelResponse([new TextContent(Text("model-1", 20))]);
        context.AddUserMessage(Text("user-2", 20));
        RecordToolExchanges(context, count: 3, resultLength: 30);

        // Act
        var result = await context.PrepareAsync();

        // Assert
        result.Messages.Select(Describe).Should().Equal("User:user-2", "Model:call_2", "Tool:call_2", "Model:call_3", "Tool:call_3");
        result.Outcome.Should().Be(PrepareOutcome.Compacted);
        result.MessagesDropped.Should().Be(4);
        result.Messages.Invoking(messages => messages.ForOpenAI()).Should().NotThrow();
    }

    [Fact]
    public async Task PrepareAsync_WhenAPinnedMessageSitsInsideTheToolLoop_KeepsItBetweenTheOpeningUserMessageAndTheNewestToolExchange()
    {
        // Arrange
        using var context = CreateContext();
        context.SetSystemPrompt(Text("system", 10));
        context.AddUserMessage(Text("user-1", 20));
        RecordToolExchanges(context, count: 1, resultLength: 30);
        context.AddPinnedMessage(MessageRole.User, Text("pin", 10));
        context.RecordModelResponse([new ToolUseContent("call_2", "search", Text("{}", 10))]);
        context.RecordToolResult("call_2", "search", Text("result", 30));

        // Act
        var result = await context.PrepareAsync();

        // Assert
        result.Messages.Select(Describe).Should().Equal("System:system", "User:user-1", "User:pin", "Model:call_2", "Tool:call_2");
        result.Outcome.Should().Be(PrepareOutcome.Compacted);
    }

    [Fact]
    public async Task PrepareAsync_WhenTheOpeningUserMessageAndTheNewestToolExchangeDoNotFit_ReportsCompactionInsufficient()
    {
        // Arrange
        using var context = CreateContext();
        context.AddUserMessage(Text("user-1", 50));
        RecordToolExchanges(context, count: 2, resultLength: 60);

        // Act
        var result = await context.PrepareAsync();

        // Assert
        result.Messages.Select(Describe).Should().Equal("User:user-1", "Model:call_2", "Tool:call_2");
        result.Outcome.Should().Be(PrepareOutcome.CompactionInsufficient);
        result.TokensAfterCompaction.Should().Be(120);
    }

    [Fact]
    public async Task PrepareAsync_WhenTheOnlyToolExchangeDoesNotFitWithItsUserMessage_ReportsCannotCompactAndSendsEveryMessage()
    {
        // Arrange
        using var context = CreateContext();
        context.AddUserMessage(Text("user-1", 50));
        RecordToolExchanges(context, count: 1, resultLength: 60);

        // Act
        var result = await context.PrepareAsync();

        // Assert
        result.Messages.Should().Equal(context.History);
        result.Outcome.Should().Be(PrepareOutcome.CannotCompact);
    }

    [Fact]
    public async Task PrepareAsync_WhenSummarizationRunsInsideALongToolLoop_KeepsTheOpeningUserMessageAfterTheSummary()
    {
        // Arrange
        using var context = CreateSummarizingContext(maxTokens: 2_000);
        context.AddUserMessage("user-1");
        context.RecordModelResponse([new TextContent("model-1")]);
        context.AddUserMessage("user-2");
        for (var call = 1; call <= 4; call++)
        {
            context.RecordModelResponse([new ToolUseContent($"call_{call}", "write", Prose(500))]);
            context.RecordToolResult($"call_{call}", "write", "done");
        }

        // Act
        var result = await context.PrepareAsync();

        // Assert
        result.Messages.Select(Describe).Should().Equal("Summary", "User:user-2", "Model:call_4", "Tool:call_4");
        result.Outcome.Should().Be(PrepareOutcome.Compacted);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    [InlineData(6, true)]
    public async Task PrepareAsync_WhenRandomToolLoopsAreCompacted_NeverReportsCompactedWithoutTheNewestUserMessage(int seed, bool summarize)
    {
        // Arrange
        var random = new Random(seed);
        using var context = summarize ? CreateSummarizingContext(maxTokens: 1_500) : CreateDefaultContext(maxTokens: 1_500);
        var violations = new List<string>();
        var compactedToolLoopViews = 0;
        var calls = 0;

        async Task PrepareAndCheckAsync()
        {
            var result = await context.PrepareAsync();
            if (result.Outcome != PrepareOutcome.Compacted)
                return;

            compactedToolLoopViews++;
            var newestUserMessage = context.History.Last(message => message.Role == MessageRole.User);
            if (!result.Messages.Contains(newestUserMessage, ReferenceEqualityComparer.Instance))
                violations.Add($"missing user message: {string.Join(" | ", result.Messages.Select(Describe))}");

            try
            {
                result.Messages.ForOpenAI();
            }
            catch (InvalidOperationException exception)
            {
                violations.Add($"{exception.Message} View: {string.Join(" | ", result.Messages.Select(Describe))}");
            }
        }

        // Act
        context.SetSystemPrompt("system");
        for (var turn = 1; turn <= 40; turn++)
        {
            context.AddUserMessage($"user-{turn} {Prose(random.Next(5, 350))}");
            await context.PrepareAsync();

            for (var toolRounds = random.Next(1, 8); toolRounds > 0; toolRounds--)
            {
                var callIds = Enumerable.Range(0, random.Next(1, 3)).Select(_ => $"call_{++calls}").ToArray();
                context.RecordModelResponse(callIds.Select(id => new ToolUseContent(id, "search", "{}")));
                foreach (var callId in callIds)
                    context.RecordToolResult(callId, "search", Prose(random.Next(5, 500)));

                await PrepareAndCheckAsync();
            }

            context.RecordModelResponse([new TextContent($"model-{turn} {Prose(random.Next(5, 200))}")]);
        }

        // Assert
        compactedToolLoopViews.Should().BeGreaterThan(50, "the seed must produce compacted views that end with a tool result");
        violations.Should().BeEmpty();
    }

    private static IConversationContext CreateDefaultContext(int maxTokens) =>
        new ConversationContextFactory(new ConversationConfigBuilder().WithMaxTokens(maxTokens).Build()).Create();

    private static IConversationContext CreateSummarizingContext(int maxTokens)
    {
        var builder = new ConversationConfigBuilder().WithMaxTokens(maxTokens).WithSlidingWindowOptions(new SlidingWindowOptions(windowSize: 2));
        var summarization = new LlmSummarizationOptions(windowSize: 2, minSummaryTokens: 10, maxSummaryTokens: 100);
        builder.SetLlmSummarizer(() => ScriptedSummarizer.Returning("summary"), "Test", summarization);

        return new ConversationContextFactory(builder.Build()).Create();
    }

    private static ConversationContext CreateContext() =>
        new(new ContextBudget(100, 0.5, emergencyThreshold: 1.0), new TextLengthTokenCounter(), StubCompactionStrategy.Unchanged());

    private static void RecordToolExchanges(IConversationContext context, int count, int resultLength)
    {
        for (var call = 1; call <= count; call++)
        {
            context.RecordModelResponse([new ToolUseContent($"call_{call}", "search", Text("{}", 10))]);
            context.RecordToolResult($"call_{call}", "search", Text("result", resultLength));
        }
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
