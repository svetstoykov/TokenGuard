using FluentAssertions;
using TokenGuard.Core;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Configuration;
using TokenGuard.Core.Models;
using TokenGuard.Core.Strategies;
using TokenGuard.Core.TokenCounting;

namespace TokenGuard.Tests.Core;

public sealed class ConversationConfigBuilderTests
{
    [Fact]
    public void Default_WithoutArguments_UsesDefaultMaxTokenBudget()
    {
        // Arrange
        var expected = ContextBudget.For(100_000);

        // Act
        var configuration = ConversationConfigBuilder.Default();

        // Assert
        configuration.Budget.Should().Be(expected);
        configuration.Counter.Should().BeOfType<EstimatedTokenCounter>();
        configuration.Strategy.Should().BeOfType<SlidingWindowStrategy>();
    }

    [Fact]
    public void Default_WithExplicitMaxTokens_UsesProvidedBudget()
    {
        // Arrange
        const int maxTokens = 200_000;

        // Act
        var configuration = ConversationConfigBuilder.Default(maxTokens);

        // Assert
        configuration.Budget.MaxTokens.Should().Be(maxTokens);
        configuration.Counter.Should().BeOfType<EstimatedTokenCounter>();
        configuration.Strategy.Should().BeOfType<SlidingWindowStrategy>();
    }

    [Fact]
    public void Build_WhenOnlyMaxTokensProvided_UsesBudgetAndDependencyDefaults()
    {
        // Arrange
        const int maxTokens = 75_000;
        var expectedBudget = ContextBudget.For(maxTokens);

        // Act
        var configuration = new ConversationConfigBuilder()
            .WithMaxTokens(maxTokens)
            .Build();

        // Assert
        configuration.Budget.Should().Be(expectedBudget);
        configuration.Counter.Should().BeOfType<EstimatedTokenCounter>();
        configuration.Strategy.Should().BeOfType<SlidingWindowStrategy>();
    }

    [Fact]
    public void BuildConfiguration_WhenOnlyMaxTokensProvided_MatchesBuildResult()
    {
        // Arrange
        var builder = new ConversationConfigBuilder()
            .WithMaxTokens(75_000);

        // Act
        var build = builder.Build();
        var buildConfiguration = builder.BuildConfiguration();

        // Assert
        buildConfiguration.Should().BeEquivalentTo(build);
        buildConfiguration.Counter.Should().BeOfType<EstimatedTokenCounter>();
        buildConfiguration.Strategy.Should().BeOfType<SlidingWindowStrategy>();
    }

    [Fact]
    public void Build_WhenOverridesProvided_UsesExplicitValuesAndInstances()
    {
        // Arrange
        var counter = new StubTokenCounter();
        var strategy = new StubCompactionStrategy();

        // Act
        var configuration = new ConversationConfigBuilder()
            .WithMaxTokens(8_192)
            .WithCompactionThreshold(0.65)
            .WithEmergencyThreshold(0.90)
            .WithReservedTokens(512)
            .WithTokenCounter(counter)
            .WithStrategy(strategy)
            .Build();

        // Assert
        configuration.Budget.MaxTokens.Should().Be(8_192);
        configuration.Budget.CompactionThreshold.Should().Be(0.65);
        configuration.Budget.EmergencyThreshold.Should().Be(0.90);
        configuration.Budget.ReservedTokens.Should().Be(512);
        configuration.Counter.Should().BeSameAs(counter);
        configuration.Strategy.Should().BeSameAs(strategy);
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
    public void BuildConfiguration_WithoutMaxTokens_ThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new ConversationConfigBuilder();

        // Act
        Action act = () => builder.BuildConfiguration();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConversationContextConfigurationBuilder*WithMaxTokens*Build()*");
    }

    [Fact]
    public void WithStrategy_WhenStrategyIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var builder = new ConversationConfigBuilder();

        // Act
        Action act = () => builder.WithStrategy(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("strategy");
    }

    [Fact]
    public void WithTokenCounter_WhenTokenCounterIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var builder = new ConversationConfigBuilder();

        // Act
        Action act = () => builder.WithTokenCounter(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("tokenCounter");
    }

    [Fact]
    public void FluentMethods_ReturnSameBuilderInstance()
    {
        // Arrange
        var builder = new ConversationConfigBuilder();
        var counter = new StubTokenCounter();
        var strategy = new StubCompactionStrategy();

        // Act
        var withMaxTokens = builder.WithMaxTokens(4_096);
        var withCompactionThreshold = builder.WithCompactionThreshold(0.70);
        var withEmergencyThreshold = builder.WithEmergencyThreshold(0.95);
        var withReservedTokens = builder.WithReservedTokens(256);
        var withTokenCounter = builder.WithTokenCounter(counter);
        var withStrategy = builder.WithStrategy(strategy);

        // Assert
        withMaxTokens.Should().BeSameAs(builder);
        withCompactionThreshold.Should().BeSameAs(builder);
        withEmergencyThreshold.Should().BeSameAs(builder);
        withReservedTokens.Should().BeSameAs(builder);
        withTokenCounter.Should().BeSameAs(builder);
        withStrategy.Should().BeSameAs(builder);
    }

    private sealed class StubTokenCounter : ITokenCounter
    {
        public int Count(ContextMessage contextMessage)
        {
            return 0;
        }

        public int Count(IEnumerable<ContextMessage> messages)
        {
            return 0;
        }
    }

    private sealed class StubCompactionStrategy : ICompactionStrategy
    {
        public Task<CompactionResult> CompactAsync(IReadOnlyList<ContextMessage> messages, ContextBudget budget, ITokenCounter tokenCounter, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new CompactionResult(messages, 0, 0, 0, nameof(StubCompactionStrategy), false));
        }
    }
}
