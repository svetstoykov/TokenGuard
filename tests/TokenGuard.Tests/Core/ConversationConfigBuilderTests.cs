using System.Runtime.CompilerServices;
using Anthropic;
using FluentAssertions;
using OpenAI.Chat;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Configuration;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;
using TokenGuard.Core.Options;
using TokenGuard.Core.Strategies;
using TokenGuard.Extensions.Anthropic;
using TokenGuard.Extensions.OpenAI;

namespace TokenGuard.Tests.Core;

public sealed class ConversationConfigBuilderTests
{
    [Fact]
    public void Default_WithoutArguments_UsesDefaultMaxTokenBudget()
    {
        // Arrange
        var expected = ContextBudget.For(100_000);
        var counter = new TrackingTokenCounter();

        // Act
        var configuration = ConversationConfigBuilder.Default();

        // Assert
        configuration.Budget.Should().Be(expected);
        configuration.StrategyFactory(counter).Should().BeOfType<TieredCompactionStrategy>();
    }

    [Fact]
    public void Default_WithExplicitMaxTokens_UsesProvidedBudget()
    {
        // Arrange
        const int maxTokens = 200_000;
        var counter = new TrackingTokenCounter();

        // Act
        var configuration = ConversationConfigBuilder.Default(maxTokens);

        // Assert
        configuration.Budget.MaxTokens.Should().Be(maxTokens);
        configuration.StrategyFactory(counter).Should().BeOfType<TieredCompactionStrategy>();
    }

    [Fact]
    public void Build_WhenOnlyMaxTokensProvided_UsesBudgetAndDependencyDefaults()
    {
        // Arrange
        const int maxTokens = 75_000;
        var expectedBudget = ContextBudget.For(maxTokens);
        var counter = new TrackingTokenCounter();

        // Act
        var configuration = new ConversationConfigBuilder()
            .WithMaxTokens(maxTokens)
            .Build();

        // Assert
        configuration.Budget.Should().Be(expectedBudget);
        configuration.StrategyFactory(counter).Should().BeOfType<TieredCompactionStrategy>();
    }

    [Fact]
    public void Build_WhenOnlyMaxTokensProvided_ReturnsFreshStrategyInstances()
    {
        // Arrange
        var configuration = new ConversationConfigBuilder()
            .WithMaxTokens(75_000)
            .Build();
        var firstCounter = new TrackingTokenCounter();
        var secondCounter = new TrackingTokenCounter();

        // Act
        var firstStrategy = configuration.StrategyFactory(firstCounter);
        var secondStrategy = configuration.StrategyFactory(secondCounter);
        firstStrategy.Should().BeOfType<TieredCompactionStrategy>();
        secondStrategy.Should().BeOfType<TieredCompactionStrategy>();
        firstStrategy.Should().NotBeSameAs(secondStrategy);
    }

    [Fact]
    public async Task Build_WhenSlidingWindowOptionsConfigured_UsesSlidingWindowOnlyWhenNoProviderIsRegistered()
    {
        // Arrange
        var oldest1 = CreateToolResultMessage("call_1", "search", "full-tool-output-1");
        var oldest2 = CreateToolResultMessage("call_2", "search", "full-tool-output-2");
        var keep1 = ContextMessage.FromText(MessageRole.User, "keep-1");
        var keep2 = ContextMessage.FromText(MessageRole.Model, "keep-2");
        var messages = new[] { oldest1, oldest2, keep1, keep2 };
        var counter = new TrackingTokenCounter();
        counter.Set(oldest1, 50);
        counter.Set(oldest2, 50);
        counter.Set(keep1, 5);
        counter.Set(keep2, 5);
        // Act
        var configuration = new ConversationConfigBuilder()
            .WithMaxTokens(8_192)
            .WithCompactionThreshold(0.65)
            .WithEmergencyThreshold(0.90)
            .WithSlidingWindowOptions(new SlidingWindowOptions(windowSize: 1, protectedWindowFraction: 0.20))
            .Build();
        var strategy = configuration.StrategyFactory(counter);
        var compacted = await strategy.CompactAsync(messages, 11);

        // Assert
        configuration.Budget.MaxTokens.Should().Be(8_192);
        configuration.Budget.CompactionThreshold.Should().Be(0.65);
        configuration.Budget.EmergencyThreshold.Should().Be(0.90);
        strategy.Should().BeOfType<TieredCompactionStrategy>();
        compacted.Messages.Should().HaveCount(4);
        compacted.Messages[0].State.Should().Be(CompactionState.Masked);
        compacted.Messages[1].State.Should().Be(CompactionState.Masked);
        compacted.Messages.Should().NotContain(message => message.State == CompactionState.Summarized);
    }

    [Fact]
    public void Build_WithoutMaxTokens_ThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new ConversationConfigBuilder();

        // Act
        Action act = () => builder.Build();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConversationContextConfigurationBuilder*WithMaxTokens*Build()*");
    }

    [Fact]
    public async Task Build_WhenLlmSummarizationIsRegistered_UsesLlmFallbackStage()
    {
        // Arrange
        var oldest1 = CreateToolResultMessage("call_1", "search", "full-tool-output-1");
        var oldest2 = CreateToolResultMessage("call_2", "search", "full-tool-output-2");
        var keep1 = ContextMessage.FromText(MessageRole.User, "keep-1");
        var keep2 = ContextMessage.FromText(MessageRole.Model, "keep-2");
        var messages = new[] { oldest1, oldest2, keep1, keep2 };
        var counter = new TrackingTokenCounter();
        counter.Set(oldest1, 50);
        counter.Set(oldest2, 50);
        counter.Set(keep1, 5);
        counter.Set(keep2, 5);
        var builder = new ConversationConfigBuilder()
            .WithMaxTokens(8_192)
            .WithSlidingWindowOptions(new SlidingWindowOptions(windowSize: 1, protectedWindowFraction: 0.20));

        // Act
        builder.SetLlmSummarizer(
            () => new TrackingSummarizer("summary-text"),
            "OpenAI",
            new LlmSummarizationOptions(windowSize: 2, minSummaryTokens: 1, maxSummaryTokens: 100));
        var configuration = builder.Build();
        var compacted = await configuration.StrategyFactory(counter).CompactAsync(messages, 11);

        // Assert
        compacted.Messages.Should().HaveCount(3);
        compacted.Messages[0].State.Should().Be(CompactionState.Summarized);
        compacted.Messages[1].Should().BeSameAs(keep1);
        compacted.Messages[2].Should().BeSameAs(keep2);
    }

    [Fact]
    public void FluentMethods_ReturnSameBuilderInstance()
    {
        // Arrange
        var builder = new ConversationConfigBuilder();

        // Act
        var withMaxTokens = builder.WithMaxTokens(4_096);
        var withCompactionThreshold = builder.WithCompactionThreshold(0.70);
        var withEmergencyThreshold = builder.WithEmergencyThreshold(0.95);
        var withSlidingWindowOptions = builder.WithSlidingWindowOptions(new SlidingWindowOptions(windowSize: 4));
        var withOverrunTolerance = builder.WithOverrunTolerance(0.10);

        // Assert
        withMaxTokens.Should().BeSameAs(builder);
        withCompactionThreshold.Should().BeSameAs(builder);
        withEmergencyThreshold.Should().BeSameAs(builder);
        withSlidingWindowOptions.Should().BeSameAs(builder);
        withOverrunTolerance.Should().BeSameAs(builder);
    }

    [Theory]
    [InlineData("OpenAIFirst")]
    [InlineData("AnthropicFirst")]
    public void UseLlmSummarization_WhenCalledTwice_ThrowsWithBothProviderNames(string order)
    {
        // Arrange
        var builder = new ConversationConfigBuilder()
            .WithMaxTokens(8_192);
        var openAiClient = new ChatClient("gpt-4.1", "test-key");
        var anthropicClient = new AnthropicClient();

        Action act = order == "OpenAIFirst"
            ? () => builder
                .UseLlmSummarization(openAiClient)
                .UseLlmSummarization(anthropicClient, "claude-3-7-sonnet-latest")
            : () => builder
                .UseLlmSummarization(anthropicClient, "claude-3-7-sonnet-latest")
                .UseLlmSummarization(openAiClient);

        // Act / Assert
        var assertion = act.Should().Throw<InvalidOperationException>();
        assertion.WithMessage("*Only one provider can be registered per builder instance*");
        assertion.Which.Message.Should().Contain("OpenAI").And.Contain("Anthropic");
    }

    [Fact]
    public void Build_WhenOverrunToleranceNotConfigured_DefaultsToFivePercent()
    {
        // Arrange

        // Act
        var configuration = new ConversationConfigBuilder()
            .WithMaxTokens(1_000)
            .Build();

        // Assert
        configuration.Budget.OverrunTolerance.Should().Be(0.05);
    }

    [Fact]
    public void Build_WhenOverrunToleranceConfigured_PropagatesValueToBudget()
    {
        // Arrange
        const double tolerance = 0.15;

        // Act
        var configuration = new ConversationConfigBuilder()
            .WithMaxTokens(10_000)
            .WithOverrunTolerance(tolerance)
            .Build();

        // Assert
        configuration.Budget.OverrunTolerance.Should().Be(tolerance);
    }

    private static ContextMessage CreateToolResultMessage(string callId, string toolName, string payload)
    {
        return new ContextMessage
        {
            Role = MessageRole.User,
            Segments = [new ToolResultContent(callId, toolName, payload)],
        };
    }

    private sealed class TrackingSummarizer : ILlmSummarizer
    {
        private readonly string _summary;

        public TrackingSummarizer(string summary)
        {
            this._summary = summary;
        }

        public Task<string> SummarizeAsync(
            IReadOnlyList<ContextMessage> messages,
            int targetTokens,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(this._summary);
        }
    }

    private sealed class TrackingTokenCounter : ITokenCounter
    {
        private readonly Dictionary<ContextMessage, int> _counts = new(ReferenceEqualityComparer.Instance);

        public void Set(ContextMessage contextMessage, int count)
        {
            this._counts[contextMessage] = count;
        }

        public int Count(ContextMessage contextMessage)
        {
            return this._counts.TryGetValue(contextMessage, out var value)
                ? value
                : 1;
        }

        public int Count(IEnumerable<ContextMessage> messages)
        {
            ArgumentNullException.ThrowIfNull(messages);

            var total = 0;
            foreach (var message in messages)
            {
                total += this.Count(message);
            }

            return total;
        }
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<ContextMessage>
    {
        public static readonly ReferenceEqualityComparer Instance = new();

        public bool Equals(ContextMessage? x, ContextMessage? y)
        {
            return ReferenceEquals(x, y);
        }

        public int GetHashCode(ContextMessage obj)
        {
            return RuntimeHelpers.GetHashCode(obj);
        }
    }
}
