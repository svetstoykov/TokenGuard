using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using TokenGuard.Core;
using TokenGuard.Core.Configuration;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Options;
using TokenGuard.Extensions.AI;
using TextContent = TokenGuard.Core.Models.Content.TextContent;

namespace TokenGuard.IntegrationTests;

public sealed class ChatClientAdapterIntegrationTests
{
    [Theory]
    [InlineData("stop", false, false)]
    [InlineData("length", false, true)]
    [InlineData("content_filter", true, false)]
    [InlineData("tool_calls", false, false)]
    [InlineData(null, false, false)]
    public async Task PrepareAsync_ChatClientSummary_MapsFinishReasonAndCarriesRequest(
        string? finishReason, bool refused, bool outputLimited)
    {
        using var client = new FakeChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Earlier work is complete."))
        {
            FinishReason = finishReason is null ? null : new ChatFinishReason(finishReason),
            Usage = new UsageDetails { InputTokenCount = 123, OutputTokenCount = 12, ReasoningTokenCount = 3 },
        });
        using var logs = new EventLoggerFactory();
        var options = new LlmSummarizationOptions(windowSize: 1, minSummaryTokens: 5, maxSummaryTokens: 100)
        {
            SummaryInstructions = "Summarize release decisions only.",
        };
        var configuration = new ConversationConfigBuilder().WithMaxTokens(1_000).WithoutEmergencyThreshold()
            .WithLoggerFactory(logs).UseLlmSummarization(client, options).Build();
        using var context = new ConversationContextFactory(configuration).Create();
        context.SetSystemPrompt("Keep the release constraints.");
        for (var turn = 0; turn < 4; turn++)
        {
            context.AddUserMessage(string.Concat(Enumerable.Repeat("Explain the release decisions. ", 100)));
            context.RecordModelResponse([new TextContent(string.Concat(Enumerable.Repeat("The release decisions are recorded. ", 100)))]);
        }
        using var cancellation = new CancellationTokenSource();

        var prepared = await context.PrepareAsync(cancellation.Token);

        client.RequestMessages.Select(message => message.Role).Should().Equal(ChatRole.System, ChatRole.User);
        client.RequestMessages[0].Text.Should().Contain("Summarize release decisions only.");
        client.RequestMessages[1].Text.Should().Contain("Transcript:");
        client.RequestOptions!.MaxOutputTokens.Should().Be(200);
        client.RequestCancellation.Should().Be(cancellation.Token);
        prepared.Messages.Should().Contain(message => message.IsPinned && message.Segments.Any(s => s.Content == "Keep the release constraints."));
        if (refused)
        {
            prepared.SummarizationError.Should().BeOfType<InvalidOperationException>();
            prepared.Messages.Should().NotContain(message => message.State == CompactionState.Summarized);
        }
        else
        {
            prepared.SummarizationError.Should().BeNull();
            prepared.Messages.Should().Contain(message => message.State == CompactionState.Summarized
                && message.Segments.Any(s => s.Content == "Earlier work is complete."));
        }
        logs.EventIds.Contains(5003).Should().Be(outputLimited);
    }

    [Fact]
    public async Task ManualToolLoop_RecordsPreparedMessagesAndMasksOlderResults()
    {
        using var client = new FakeChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("one", "search")])));
        var configuration = new ConversationConfigBuilder().WithMaxTokens(1_000).WithoutEmergencyThreshold().WithoutToolResultLimit()
            .WithSlidingWindowOptions(new SlidingWindowOptions(windowSize: 1, protectedWindowFraction: 0.25)).Build();
        using var context = new ConversationContextFactory(configuration).Create();
        context.SetSystemPrompt("Keep constraints.");
        context.AddUserMessage("Search the repository.");
        var initial = await context.PrepareAsync();
        var response = await client.GetResponseAsync(initial.Messages.ForChatClient());
        context.RecordModelResponse(response.ResponseSegments(), response.InputTokens());
        context.RecordToolResult("one", "search", string.Concat(Enumerable.Repeat("Large repository result. ", 300)));
        context.RecordModelResponse([new TextContent("Search is complete.")]);
        context.AddUserMessage("Continue with the next step.");

        var prepared = await context.PrepareAsync();
        var messages = prepared.Messages.ForChatClient();

        prepared.Messages.Should().Contain(message => message.State == CompactionState.Masked);
        messages.First().Text.Should().Be("Keep constraints.");
        messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>().Should().ContainSingle()
            .Which.Result.Should().BeOfType<string>().Which.Should().Contain("search");
        client.RequestMessages.Select(message => message.Role).Should().Equal(ChatRole.System, ChatRole.User);
    }

    [Fact]
    public void UseLlmSummarization_SecondClient_Throws()
    {
        using var client = new FakeChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "summary")));
        var builder = new ConversationConfigBuilder().UseLlmSummarization(client);

        var act = () => builder.UseLlmSummarization(client);

        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(null, "not reported")]
    [InlineData(-1L, "not reported")]
    [InlineData(0L, "0")]
    [InlineData(12L, "12")]
    [InlineData(long.MaxValue, "2147483647")]
    public async Task PrepareAsync_SummaryUsage_PreservesKnownZeroAndBoundsLargeCounts(long? usage, string expected)
    {
        using var client = new FakeChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, ""))
        {
            FinishReason = ChatFinishReason.Stop,
            Usage = usage.HasValue ? new UsageDetails { OutputTokenCount = usage, ReasoningTokenCount = usage } : null,
        });
        var configuration = new ConversationConfigBuilder().WithMaxTokens(1_000).WithoutEmergencyThreshold()
            .UseLlmSummarization(client, new LlmSummarizationOptions(windowSize: 1, minSummaryTokens: 5, maxSummaryTokens: 100)).Build();
        using var context = new ConversationContextFactory(configuration).Create();
        for (var turn = 0; turn < 4; turn++)
        {
            context.AddUserMessage(string.Concat(Enumerable.Repeat("Explain the release decisions. ", 100)));
            context.RecordModelResponse([new TextContent(string.Concat(Enumerable.Repeat("The release decisions are recorded. ", 100)))]);
        }

        var prepared = await context.PrepareAsync();

        prepared.SummarizationError.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Contain($"output tokens: {expected}; reasoning tokens: {expected}.");
    }

    /// <summary>Represents a deterministic model boundary that captures the public chat request.</summary>
    /// <param name="response">The response returned by the client.</param>
    private sealed class FakeChatClient(ChatResponse response) : IChatClient
    {
        /// <summary>Gets the latest request's messages.</summary>
        public IReadOnlyList<ChatMessage> RequestMessages { get; private set; } = [];
        /// <summary>Gets the latest request's options.</summary>
        public ChatOptions? RequestOptions { get; private set; }
        /// <summary>Gets the latest request's cancellation token.</summary>
        public CancellationToken RequestCancellation { get; private set; }

        /// <inheritdoc />
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            this.RequestMessages = messages.ToArray();
            this.RequestOptions = options;
            this.RequestCancellation = cancellationToken;
            return Task.FromResult(response);
        }

        /// <inheritdoc />
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        /// <inheritdoc />
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        /// <inheritdoc />
        public void Dispose() { }
    }

    private sealed class EventLoggerFactory : ILoggerFactory
    {
        /// <summary>Gets the observed log event identifiers.</summary>
        public List<int> EventIds { get; } = [];
        /// <inheritdoc />
        public ILogger CreateLogger(string categoryName) => new EventLogger(this.EventIds);
        /// <inheritdoc />
        public void AddProvider(ILoggerProvider provider) { }
        /// <inheritdoc />
        public void Dispose() { }

        private sealed class EventLogger(List<int> eventIds) : ILogger
        {
            /// <inheritdoc />
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            /// <inheritdoc />
            public bool IsEnabled(LogLevel logLevel) => true;
            /// <inheritdoc />
            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => eventIds.Add(eventId.Id);
        }
    }
}
