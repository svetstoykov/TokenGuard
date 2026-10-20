using System.Runtime.CompilerServices;
using Anthropic;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using TokenGuard.Core;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Configuration;
using TokenGuard.Core.Diagnostics;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;
using TokenGuard.Core.Options;
using TokenGuard.Core.Strategies;
using TokenGuard.Core.Summarization;
using TokenGuard.Extensions.Anthropic;
using TokenGuard.Extensions.OpenAI;
using TokenGuard.Tests.Diagnostics;
using TokenGuard.Tests.Summarization;

namespace TokenGuard.Tests.Core;

public sealed class ConversationConfigBuilderTests
{
    [Fact]
    public void Default_WithoutArguments_UsesDefaultMaxTokenBudget()
    {
        // Arrange
        var expected = ContextBudget.For(40_000);
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
    public async Task Build_WithSlidingWindowOptionsNamingExemptTools_LeavesTheirResultsUnmasked()
    {
        // Arrange
        var notes = CreateToolResultMessage("call_1", "notes", "full-notes-output");
        var search = CreateToolResultMessage("call_2", "search", "full-tool-output-2");
        var keep1 = ContextMessage.FromText(MessageRole.User, "keep-1");
        var keep2 = ContextMessage.FromText(MessageRole.Model, "keep-2");
        var messages = new[] { notes, search, keep1, keep2 };
        var counter = new TrackingTokenCounter();
        counter.Set(notes, 50);
        counter.Set(search, 50);
        counter.Set(keep1, 5);
        counter.Set(keep2, 5);
        var options = new SlidingWindowOptions(windowSize: 1, protectedWindowFraction: 0.20) { ExemptToolNames = ["notes"] };
        var configuration = new ConversationConfigBuilder().WithMaxTokens(8_192).WithSlidingWindowOptions(options).Build();

        // Act
        var compacted = await configuration.StrategyFactory(counter).CompactAsync(messages, 11);

        // Assert
        compacted.Messages[0].Should().BeSameAs(notes);
        compacted.Messages[1].State.Should().Be(CompactionState.Masked);
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
        builder.UseLlmSummarization(
            new TrackingSummarizer("summary-text"), new LlmSummarizationOptions(windowSize: 2, minSummaryTokens: 1, maxSummaryTokens: 100));
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
        var withoutEmergencyThreshold = builder.WithoutEmergencyThreshold();
        var withSlidingWindowOptions = builder.WithSlidingWindowOptions(new SlidingWindowOptions(windowSize: 4));
        var withOverrunTolerance = builder.WithOverrunTolerance(0.10);

        // Assert
        withMaxTokens.Should().BeSameAs(builder);
        withCompactionThreshold.Should().BeSameAs(builder);
        withEmergencyThreshold.Should().BeSameAs(builder);
        withoutEmergencyThreshold.Should().BeSameAs(builder);
        withSlidingWindowOptions.Should().BeSameAs(builder);
        withOverrunTolerance.Should().BeSameAs(builder);
    }

    [Fact]
    public void WithLoggerFactory_ReturnsSameBuilderInstance()
    {
        // Arrange
        var builder = new ConversationConfigBuilder();

        // Act
        var returned = builder.WithLoggerFactory(new CapturingLoggerFactory());

        // Assert
        returned.Should().BeSameAs(builder);
    }

    [Fact]
    public void WithLoggerFactory_WhenLoggerFactoryIsNull_Throws()
    {
        // Arrange
        var builder = new ConversationConfigBuilder();

        // Act
        var act = () => builder.WithLoggerFactory(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("loggerFactory");
    }

    [Fact]
    public void WithLoggerFactory_ContextCreatedFromConfiguration_LogsThroughSuppliedFactory()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        var configuration = new ConversationConfigBuilder()
            .WithMaxTokens(8_192)
            .WithLoggerFactory(logs)
            .Build();
        using var context = new ConversationContextFactory(configuration).Create();

        // Act
        context.AddUserMessage("hello");

        // Assert
        logs.Records.Should().ContainSingle().Which.Level.Should().Be(LogLevel.Trace);
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
    public void UseLlmSummarization_WhenACustomSummarizerIsAlreadyRegistered_ThrowsNamingBothSummarizers()
    {
        // Arrange
        var builder = new ConversationConfigBuilder()
            .WithMaxTokens(8_192)
            .UseLlmSummarization(RespondingSummarizer.Answering("summary"));

        // Act
        Action act = () => builder.UseLlmSummarization(new ChatClient("gpt-4.1", "test-key"));

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*'RespondingSummarizer'*Only one provider can be registered per builder instance*'OpenAISummarizer'*");
    }

    [Fact]
    public void UseLlmSummarization_WithANullSummarizer_ThrowsArgumentNullException()
    {
        // Arrange
        var builder = new ConversationConfigBuilder().WithMaxTokens(8_192);

        // Act
        Action act = () => builder.UseLlmSummarization((ILlmSummarizer)null!);

        // Assert
        act.Should().ThrowExactly<ArgumentNullException>().WithParameterName("summarizer");
    }

    [Fact]
    public async Task UseLlmSummarization_WithACustomSummarizer_ServesEveryContextWithTheSameInstance()
    {
        // Arrange
        var summarizer = RespondingSummarizer.Answering("summary");
        var configuration = new ConversationConfigBuilder()
            .WithMaxTokens(1_000)
            .UseLlmSummarization(summarizer, new LlmSummarizationOptions(windowSize: 2, minSummaryTokens: 5, maxSummaryTokens: 50))
            .Build();
        var first = configuration.StrategyFactory(new TextLengthTokenCounter());
        var second = configuration.StrategyFactory(new TextLengthTokenCounter());

        // Act
        var firstResult = await first.CompactAsync(CreateTextHistory(), 80);
        var secondResult = await second.CompactAsync(CreateTextHistory(), 80);

        // Assert
        summarizer.Requests.Should().HaveCount(2);
        firstResult.Messages[0].State.Should().Be(CompactionState.Summarized);
        secondResult.Messages[0].State.Should().Be(CompactionState.Summarized);
    }

    [Fact]
    public async Task UseLlmSummarization_WithSummaryInstructions_SendsThemInTheSystemPrompt()
    {
        // Arrange
        var summarizer = RespondingSummarizer.Answering("summary");
        var options = new LlmSummarizationOptions(windowSize: 2, minSummaryTokens: 5, maxSummaryTokens: 50)
        {
            SummaryInstructions = "Summarize the decisions only.",
        };
        var configuration = new ConversationConfigBuilder().WithMaxTokens(1_000).UseLlmSummarization(summarizer, options).Build();

        // Act
        await configuration.StrategyFactory(new TextLengthTokenCounter()).CompactAsync(CreateTextHistory(), 80);

        // Assert
        summarizer.Requests.Should().ContainSingle().Which.SystemPrompt.Should().StartWith("Summarize the decisions only.\n\n");
    }

    [Fact]
    public async Task UseLlmSummarization_WithACustomSummarizer_LogsItsCallsUnderTheSummarizerTypeName()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        var configuration = new ConversationConfigBuilder()
            .WithMaxTokens(1_000)
            .UseLlmSummarization(
                RespondingSummarizer.Answering("summary"), new LlmSummarizationOptions(windowSize: 2, minSummaryTokens: 5, maxSummaryTokens: 50))
            .Build();
        var strategy = configuration.DiagnosticStrategyFactory!(new TextLengthTokenCounter(), new ConversationDiagnostics(logs, "default"));

        // Act
        await strategy.CompactAsync(CreateTextHistory(), 80);

        // Assert
        var starting = logs.WithEventId(5000).Should().ContainSingle().Subject;
        starting.Category.Should().Be(typeof(RespondingSummarizer).FullName);
        starting.Property("Provider").Should().Be(nameof(RespondingSummarizer));
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

    [Fact]
    public void Build_WhenEmergencyThresholdNotConfigured_UsesLibraryDefault()
    {
        // Arrange

        // Act
        var configuration = new ConversationConfigBuilder()
            .WithMaxTokens(10_000)
            .Build();

        // Assert
        configuration.Budget.EmergencyThreshold.Should().Be(1.0);
        configuration.Budget.EmergencyTriggerTokens.Should().Be(10_000);
    }

    [Fact]
    public void WithoutEmergencyThreshold_DisablesEmergencyTruncation()
    {
        // Arrange

        // Act
        var configuration = new ConversationConfigBuilder()
            .WithMaxTokens(10_000)
            .WithoutEmergencyThreshold()
            .Build();

        // Assert
        configuration.Budget.EmergencyThreshold.Should().BeNull();
        configuration.Budget.EmergencyTriggerTokens.Should().BeNull();
    }

    [Fact]
    public void WithEmergencyThreshold_AfterWithoutEmergencyThreshold_ReEnablesEmergencyTruncation()
    {
        // Arrange

        // Act
        var configuration = new ConversationConfigBuilder()
            .WithMaxTokens(10_000)
            .WithoutEmergencyThreshold()
            .WithEmergencyThreshold(0.95)
            .Build();

        // Assert
        configuration.Budget.EmergencyThreshold.Should().Be(0.95);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Build_WhenEmergencyThresholdIsNotFinite_ThrowsNamingEmergencyThreshold(double emergencyThreshold)
    {
        // Arrange
        var builder = new ConversationConfigBuilder().WithMaxTokens(1_000).WithEmergencyThreshold(emergencyThreshold);

        // Act
        Action act = () => builder.Build();

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("emergencyThreshold").WithMessage("*EmergencyThreshold*");
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Build_WhenCompactionThresholdIsNotFinite_ThrowsNamingCompactionThreshold(double compactionThreshold)
    {
        // Arrange
        var builder = new ConversationConfigBuilder().WithMaxTokens(1_000).WithCompactionThreshold(compactionThreshold);

        // Act
        Action act = () => builder.Build();

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("compactionThreshold").WithMessage("*CompactionThreshold*");
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void Build_WhenEmergencyThresholdIsOutOfRange_ThrowsNamingEmergencyThreshold(double emergencyThreshold)
    {
        // Arrange
        var builder = new ConversationConfigBuilder().WithMaxTokens(1_000).WithEmergencyThreshold(emergencyThreshold);

        // Act
        Action act = () => builder.Build();

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("emergencyThreshold")
            .WithMessage("EmergencyThreshold must be in the range*");
    }

    [Fact]
    public void Build_WhenCompactionThresholdReachesDefaultEmergencyThreshold_ThrowsNamingCompactionThresholdWithBothValues()
    {
        // Arrange
        var builder = new ConversationConfigBuilder().WithMaxTokens(1_000).WithCompactionThreshold(1);

        // Act
        Action act = () => builder.Build();

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("compactionThreshold")
            .WithMessage("CompactionThreshold (1) must be less than EmergencyThreshold (1)*");
    }

    [Fact]
    public void Build_WhenSlidingWindowOptionsAreUninitialized_ThrowsNamingTheOptionType()
    {
        // Arrange
        var builder = new ConversationConfigBuilder().WithMaxTokens(1_000).WithSlidingWindowOptions(default);

        // Act
        Action act = () => builder.Build();

        // Assert
        act.Should().ThrowExactly<ArgumentException>().WithParameterName("options").WithMessage("*SlidingWindowOptions*not initialized*");
    }

    [Fact]
    public void Build_WhenLlmSummarizationOptionsAreUninitialized_ThrowsNamingTheOptionType()
    {
        // Arrange
        var builder = new ConversationConfigBuilder().WithMaxTokens(1_000);
        builder.UseLlmSummarization(new TrackingSummarizer("summary-text"), default(LlmSummarizationOptions));

        // Act
        Action act = () => builder.Build();

        // Assert
        act.Should().ThrowExactly<ArgumentException>().WithParameterName("options").WithMessage("*LlmSummarizationOptions*not initialized*");
    }

    [Theory]
    [InlineData(40_000, 10_000)]
    [InlineData(200_000, 25_000)]
    [InlineData(3, 1)]
    public void Build_WhenTheToolResultLimitIsNotConfigured_UsesTheSmallerOf25000AndAQuarterOfMaxTokens(int maxTokens, int expected)
    {
        // Arrange
        var builder = new ConversationConfigBuilder().WithMaxTokens(maxTokens);

        // Act
        var configuration = builder.Build();

        // Assert
        configuration.MaxToolResultTokens.Should().Be(expected);
    }

    [Theory]
    [InlineData(40_000, 10_000)]
    [InlineData(200_000, 25_000)]
    public void Default_UsesTheDefaultToolResultLimitForItsMaxTokens(int maxTokens, int expected)
    {
        // Arrange

        // Act
        var configuration = ConversationConfigBuilder.Default(maxTokens);

        // Assert
        configuration.MaxToolResultTokens.Should().Be(expected);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(1_000_000)]
    public void WithMaxToolResultTokens_UsesTheGivenLimitEvenAboveTheBudget(int maxToolResultTokens)
    {
        // Arrange
        var builder = new ConversationConfigBuilder().WithMaxTokens(40_000);

        // Act
        var configuration = builder.WithMaxToolResultTokens(maxToolResultTokens).Build();

        // Assert
        configuration.MaxToolResultTokens.Should().Be(maxToolResultTokens);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void WithMaxToolResultTokens_WhenTheLimitIsNotPositive_ThrowsArgumentOutOfRangeException(int maxToolResultTokens)
    {
        // Arrange
        var builder = new ConversationConfigBuilder();

        // Act
        Action act = () => builder.WithMaxToolResultTokens(maxToolResultTokens);

        // Assert
        act.Should().ThrowExactly<ArgumentOutOfRangeException>().WithParameterName("maxToolResultTokens");
    }

    [Fact]
    public void WithoutToolResultLimit_TurnsTheLimitOff()
    {
        // Arrange
        var builder = new ConversationConfigBuilder().WithMaxTokens(40_000);

        // Act
        var configuration = builder.WithoutToolResultLimit().Build();

        // Assert
        configuration.MaxToolResultTokens.Should().BeNull();
    }

    [Fact]
    public void WithMaxToolResultTokens_AfterWithoutToolResultLimit_TurnsTheLimitBackOn()
    {
        // Arrange
        var builder = new ConversationConfigBuilder().WithMaxTokens(40_000).WithoutToolResultLimit();

        // Act
        var configuration = builder.WithMaxToolResultTokens(2_000).Build();

        // Assert
        configuration.MaxToolResultTokens.Should().Be(2_000);
    }

    [Fact]
    public void WithoutToolResultLimit_AfterWithMaxToolResultTokens_TurnsTheLimitOff()
    {
        // Arrange
        var builder = new ConversationConfigBuilder().WithMaxTokens(40_000).WithMaxToolResultTokens(2_000);

        // Act
        var configuration = builder.WithoutToolResultLimit().Build();

        // Assert
        configuration.MaxToolResultTokens.Should().BeNull();
    }

    private static ContextMessage[] CreateTextHistory() =>
        Enumerable.Range(0, 5)
            .Select(index => ContextMessage.FromText(index % 2 == 0 ? MessageRole.User : MessageRole.Model, $"message-{index}".PadRight(20, 'x')))
            .ToArray();

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

        public List<SummarizerRequest> Requests { get; } = [];

        public Task<SummarizerResponse> SummarizeAsync(SummarizerRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (this.Requests)
                this.Requests.Add(request);

            return Task.FromResult(new SummarizerResponse(this._summary, SummarizerFinishReason.Completed));
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
