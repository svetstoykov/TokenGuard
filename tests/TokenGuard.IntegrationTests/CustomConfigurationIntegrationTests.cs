using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TokenGuard.Core;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Configuration;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Extensions;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;
using TokenGuard.Core.Options;
using TokenGuard.Core.Summarization;
using TokenGuard.Core.TokenCounting;

namespace TokenGuard.IntegrationTests;

public sealed class CustomConfigurationIntegrationTests
{
    [Fact]
    public async Task CustomCounter_RecordsMessagesAndAppliesProviderCorrectionOnItsScale()
    {
        // Arrange
        var counter = new DoubleEstimateCounter();
        var configuration = new ConversationConfigBuilder().WithMaxTokens(10_000).WithTokenCounter(counter).Build();
        using var context = new ConversationContextFactory(configuration).Create();
        context.AddUserMessage("Explain the release.");
        var first = await context.PrepareAsync();

        // Act
        context.RecordModelResponse([new TextContent("The release is ready.")], providerInputTokens: first.TokensAfterCompaction + 37);
        var corrected = await context.PrepareAsync();

        // Assert
        context.History.Should().OnlyContain(message => message.TokenCount == counter.Count(message));
        first.TokensAfterCompaction.Should().Be(2 * new EstimatedTokenCounter().Count(
            ContextMessage.FromText(MessageRole.User, "Explain the release.")));
        corrected.TokensAfterCompaction.Should().Be(context.History.Sum(counter.Count) + 37);
    }

    [Fact]
    public void CustomCounter_ToolResultLimitUsesItsScale()
    {
        // Arrange
        var counter = new DoubleEstimateCounter();
        var content = string.Join("\n", Enumerable.Repeat("Release notes describe the completed work.", 30));
        var original = ContextMessage.FromContent(MessageRole.Tool, new ToolResultContent("call", "read", content));
        var configuration = new ConversationConfigBuilder().WithMaxTokens(10_000).WithTokenCounter(counter)
            .WithMaxToolResultTokens(counter.Count(original) - 1).Build();
        using var context = new ConversationContextFactory(configuration).Create();
        context.AddUserMessage("Read release notes.");
        context.RecordModelResponse([new ToolUseContent("call", "read", "{}")]);

        // Act
        context.RecordToolResult("call", "read", content);

        // Assert
        var stored = context.History.Last();
        stored.Segments.Single().Content.Should().Contain("Tool result shortened");
        stored.TokenCount.Should().Be(counter.Count(stored));
        stored.TokenCount.Should().BeLessThanOrEqualTo(configuration.MaxToolResultTokens!.Value);
        new EstimatedTokenCounter().Count(original).Should().BeLessThan(configuration.MaxToolResultTokens.Value);
    }

    [Fact]
    public async Task CustomCounter_EmergencyTruncationUsesItsScale()
    {
        // Arrange
        var configuration = new ConversationConfigBuilder().WithMaxTokens(20).WithTokenCounter(new CharacterCounter()).Build();
        using var context = new ConversationContextFactory(configuration).Create();
        context.AddUserMessage("An older question that exceeds the budget.");
        context.RecordModelResponse([new TextContent("Older answer.")]);
        context.AddUserMessage("Continue.");

        // Act
        var prepared = await context.PrepareAsync();

        // Assert
        prepared.MessagesDropped.Should().Be(2);
        prepared.Messages.Should().ContainSingle().Which.Segments.Single().Content.Should().Be("Continue.");
        prepared.TokensAfterCompaction.Should().Be(9);
    }

    [Fact]
    public async Task CustomCounter_SummarizerTranscriptUsesItsRequestBudget()
    {
        // Arrange
        var counter = new DoubleEstimateCounter();
        var summarizer = new RequestCapture();
        var configuration = new ConversationConfigBuilder().WithMaxTokens(2_000).WithTokenCounter(counter).WithoutEmergencyThreshold()
            .UseLlmSummarization(summarizer, new LlmSummarizationOptions(windowSize: 1, minSummaryTokens: 5, maxSummaryTokens: 100)).Build();
        using var context = new ConversationContextFactory(configuration).Create();
        for (var turn = 0; turn < 8; turn++)
        {
            context.AddUserMessage($"Release question {turn}: " + string.Concat(Enumerable.Repeat("Explain release decisions. ", 40)));
            context.RecordModelResponse([new TextContent(string.Concat(Enumerable.Repeat("The decisions are recorded. ", 40)))]);
        }

        // Act
        var prepared = await context.PrepareAsync();

        // Assert
        prepared.Outcome.Should().Be(PrepareOutcome.Compacted);
        var request = summarizer.Requests.Should().ContainSingle().Subject;
        var requestTokens = counter.Count(ContextMessage.FromText(MessageRole.User, request.SystemPrompt))
            + counter.Count(ContextMessage.FromText(MessageRole.User, request.UserPrompt));
        requestTokens.Should().BeLessThanOrEqualTo(configuration.Budget.MaxTokens);
        request.UserPrompt.Should().NotContain("Release question 0:");
        prepared.Messages.Where(message => message.State == CompactionState.Summarized).Should().ContainSingle();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CustomStrategy_UsesResolvedLoggerAndFreshConversationState(bool explicitlyConfigured)
    {
        // Arrange
        using var containerLogs = new CaptureLoggerFactory();
        using var configuredLogs = new CaptureLoggerFactory();
        var created = new List<CompactionStrategyContext>();
        var services = new ServiceCollection().AddSingleton<ILoggerFactory>(containerLogs);
        services.AddConversationContext("release", builder =>
        {
            builder.WithMaxTokens(20).WithoutEmergencyThreshold().WithTokenCounter(new CharacterCounter())
                .WithCompactionStrategy(context =>
                {
                    created.Add(context);
                    return new KeepLastStrategy(context.TokenCounter, context.LoggerFactory.CreateLogger("ReleaseStrategy"));
                });
            if (explicitlyConfigured)
                builder.WithLoggerFactory(configuredLogs);
        });
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IConversationContextFactory>();
        using var first = factory.Create("release");
        using var second = factory.Create("release");
        first.AddUserMessage("An older release question that exceeds the budget.");
        first.RecordModelResponse([new TextContent("An older answer.")]);
        first.AddUserMessage("Continue.");

        // Act
        var prepared = await first.PrepareAsync();
        await first.PrepareAsync();

        // Assert
        created.Should().HaveCount(2);
        created.Should().OnlyContain(context => context.ContextName == "release");
        created[0].TokenCounter.Should().NotBeSameAs(created[1].TokenCounter);
        prepared.Outcome.Should().Be(PrepareOutcome.Compacted);
        prepared.Messages.Should().ContainSingle().Which.Segments.Single().Content.Should().Be("Continue.");
        prepared.TokensAfterCompaction.Should().Be(9);
        var expectedLogs = explicitlyConfigured ? configuredLogs : containerLogs;
        expectedLogs.Messages.Should().Contain("Custom strategy compacted.");
        var otherLogs = explicitlyConfigured ? containerLogs : configuredLogs;
        otherLogs.Messages.Should().NotContain("Custom strategy compacted.");
        second.History.Should().BeEmpty();
    }

    [Fact]
    public async Task PositionalConfigurationConstructor_UsesItsStrategyAndTheDefaultCounter()
    {
        // Arrange
        var configuration = new ConversationContextConfiguration(ContextBudget.For(20),
            counter => new KeepLastStrategy(counter, NullLogger.Instance));
        using var context = new ConversationContextFactory(configuration).Create();
        context.AddUserMessage(string.Concat(Enumerable.Repeat("Release work is ready. ", 20)));
        context.RecordModelResponse([new TextContent("Done.")]);
        context.AddUserMessage("Next.");

        // Act
        var prepared = await context.PrepareAsync();

        // Assert
        prepared.Outcome.Should().Be(PrepareOutcome.Compacted);
        prepared.Messages.Should().ContainSingle().Which.Segments.Single().Content.Should().Be("Next.");
        prepared.TokensAfterCompaction.Should().Be(new EstimatedTokenCounter().Count(prepared.Messages));
    }

    [Fact]
    public async Task DefaultCounter_PreparesTheSameViewsAsAnExplicitBalancedEstimator()
    {
        // Arrange
        var defaults = new ConversationConfigBuilder().WithMaxTokens(600).Build();
        var explicitCounter = new ConversationConfigBuilder().WithMaxTokens(600).WithTokenCounter(new EstimatedTokenCounter()).Build();
        using var first = new ConversationContextFactory(defaults).Create();
        using var second = new ConversationContextFactory(explicitCounter).Create();
        foreach (var context in new[] { first, second })
        {
            context.SetSystemPrompt("Keep release decisions.");
            context.AddUserMessage("Read the release files.");
            for (var turn = 0; turn < 8; turn++)
            {
                context.RecordModelResponse([new ToolUseContent($"call-{turn}", "read", "{}")]);
                context.RecordToolResult($"call-{turn}", "read", string.Concat(Enumerable.Repeat("Release notes. ", 80)));
            }
        }

        // Act
        var defaultView = await first.PrepareAsync();
        var explicitView = await second.PrepareAsync();

        // Assert
        defaultView.Outcome.Should().Be(explicitView.Outcome);
        defaultView.TokensAfterCompaction.Should().Be(explicitView.TokensAfterCompaction);
        defaultView.Messages.Select(Describe).Should().Equal(explicitView.Messages.Select(Describe));
    }

    [Fact]
    public async Task ConfigurationCopy_ReplacingStrategyFactoryUsesTheReplacement()
    {
        // Arrange
        var configured = new ConversationConfigBuilder().WithMaxTokens(20).WithoutEmergencyThreshold()
            .WithCompactionStrategy(_ => throw new InvalidOperationException("The replaced factory must not run.")).Build();
        var replacement = configured with { StrategyFactory = counter => new KeepLastStrategy(counter, NullLogger.Instance) };
        using var context = new ConversationContextFactory(replacement).Create();
        context.AddUserMessage(string.Concat(Enumerable.Repeat("Earlier release work. ", 30)));
        context.AddUserMessage("Next.");

        // Act
        var prepared = await context.PrepareAsync();

        // Assert
        prepared.Outcome.Should().Be(PrepareOutcome.Compacted);
        prepared.Messages.Should().ContainSingle().Which.Segments.Single().Content.Should().Be("Next.");
    }

    private static string Describe(ContextMessage message) =>
        $"{message.Role}|{message.State}|{message.TokenCount}|{string.Join("|", message.Segments.Select(segment => segment.Content))}";

    /// <summary>Represents a counter with twice the default estimate, honoring already cached counts.</summary>
    private sealed class DoubleEstimateCounter : ITokenCounter
    {
        private readonly EstimatedTokenCounter _estimate = new();

        /// <inheritdoc />
        public int Count(ContextMessage contextMessage) => contextMessage.TokenCount ?? 2 * this._estimate.Count(contextMessage);

        /// <inheritdoc />
        public int Count(IEnumerable<ContextMessage> messages) => messages.Sum(this.Count);
    }

    /// <summary>Represents a deterministic character counter.</summary>
    private sealed class CharacterCounter : ITokenCounter
    {
        /// <inheritdoc />
        public int Count(ContextMessage contextMessage) => contextMessage.Segments.Sum(segment => segment.Content.Length);

        /// <inheritdoc />
        public int Count(IEnumerable<ContextMessage> messages) => messages.Sum(this.Count);
    }

    /// <summary>Represents a strategy that retains only the last compactable message.</summary>
    private sealed class KeepLastStrategy(ITokenCounter counter, ILogger logger) : ICompactionStrategy
    {
        /// <inheritdoc />
        public Task<CompactionResult> CompactAsync(
            IReadOnlyList<ContextMessage> messages, int availableTokens, CancellationToken cancellationToken = default)
        {
            logger.LogInformation("Custom strategy compacted.");
            var kept = messages.TakeLast(1).ToArray();
            return Task.FromResult(new CompactionResult(kept, counter.Count(messages), counter.Count(kept), messages.Count - 1, "KeepLast"));
        }
    }

    /// <summary>Represents a model boundary that captures its requests.</summary>
    private sealed class RequestCapture : ILlmSummarizer
    {
        /// <summary>Gets the requests received by the summarizer.</summary>
        public List<SummarizerRequest> Requests { get; } = [];

        /// <inheritdoc />
        public Task<SummarizerResponse> SummarizeAsync(SummarizerRequest request, CancellationToken cancellationToken = default)
        {
            this.Requests.Add(request);
            return Task.FromResult(new SummarizerResponse("Release work is complete.", SummarizerFinishReason.Completed));
        }
    }

    /// <summary>Represents a logger factory that captures messages.</summary>
    private sealed class CaptureLoggerFactory : ILoggerFactory
    {
        /// <summary>Gets the captured messages.</summary>
        public List<string> Messages { get; } = [];

        /// <inheritdoc />
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(this.Messages);

        /// <inheritdoc />
        public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException();

        /// <inheritdoc />
        public void Dispose() { }

        private sealed class CaptureLogger(List<string> messages) : ILogger
        {
            /// <inheritdoc />
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            /// <inheritdoc />
            public bool IsEnabled(LogLevel logLevel) => true;

            /// <inheritdoc />
            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => messages.Add(formatter(state, exception));
        }
    }
}
