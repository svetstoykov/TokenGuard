using TokenGuard.Core.Abstractions;
using FluentAssertions;
using System.Reflection;
using TokenGuard.Core;
using TokenGuard.Core.Configuration;
using TokenGuard.Core.Models;

namespace TokenGuard.Tests.Core;

public sealed class ConversationContextFactoryTests
{
    [Fact]
    public void Create_ReturnsNewInstanceOnEachCall()
    {
        // Arrange
        IConversationContextFactory factory = CreateFactory();

        // Act
        using var first = factory.Create();
        using var second = factory.Create();

        // Assert
        first.Should().NotBeSameAs(second);
    }

    [Fact]
    public void Create_InvokesFreshFactoriesOnEachCall()
    {
        // Arrange
        var strategyCalls = 0;
        var observerCalls = 0;
        var factory = new ConversationContextFactory(new ConversationContextConfiguration(
            ContextBudget.For(100_000),
            () =>
            {
                strategyCalls++;
                return new StubCompactionStrategy();
            },
            () =>
            {
                observerCalls++;
                return new StubCompactionObserver();
            }));

        // Act
        using var first = (ConversationContext)factory.Create();
        using var second = (ConversationContext)factory.Create();
        var firstCounter = GetPrivateField<ITokenCounter>(first, "_counter");
        var secondCounter = GetPrivateField<ITokenCounter>(second, "_counter");

        // Assert
        strategyCalls.Should().Be(2);
        observerCalls.Should().Be(2);
        firstCounter.Should().NotBeSameAs(secondCounter);
        GetPrivateField<ICompactionStrategy>(first, "_strategy").Should().NotBeSameAs(GetPrivateField<ICompactionStrategy>(second, "_strategy"));
        GetPrivateField<ICompactionObserver?>(first, "_observer").Should().NotBeSameAs(GetPrivateField<ICompactionObserver?>(second, "_observer"));
    }

    [Fact]
    public void Create_ReturnedInstanceIsNotDisposed()
    {
        // Arrange
        IConversationContextFactory factory = CreateFactory();

        // Act
        using var ctx = factory.Create();

        // Assert
        _ = ctx.History;
    }

    [Fact]
    public void CreateNamed_UsesRegisteredConfigurationBudget()
    {
        // Arrange
        var config = new ConversationConfigBuilder()
            .WithMaxTokens(200_000)
            .Build();

        IConversationContextFactory factory = CreateFactory()
            .AddNamed("large", config);

        // Act
        using var ctx = factory.Create("large");

        // Assert
        ctx.Should().NotBeNull();
        ctx.History.Should().BeEmpty();
    }

    [Fact]
    public void CreateNamed_ReturnsNewInstanceOnEachCall()
    {
        // Arrange
        var config = new ConversationConfigBuilder()
            .WithMaxTokens(50_000)
            .Build();

        IConversationContextFactory factory = CreateFactory()
            .AddNamed("small", config);

        // Act
        using var first = factory.Create("small");
        using var second = factory.Create("small");

        // Assert
        first.Should().NotBeSameAs(second);
    }

    [Fact]
    public void CreateNamed_WithUnregisteredName_ThrowsInvalidOperationException()
    {
        // Arrange
        IConversationContextFactory factory = CreateFactory();

        // Act
        Action act = () => factory.Create("unknown");

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*unknown*");
    }

    [Fact]
    public void AddNamed_IsFluentAndReturnsSameFactory()
    {
        // Arrange
        var config = new ConversationConfigBuilder()
            .WithMaxTokens(100_000)
            .Build();

        var factory = CreateFactory();

        // Act
        var returned = factory.AddNamed("default", config);

        // Assert
        returned.Should().BeSameAs(factory);
    }

    [Fact]
    public void Build_WithoutMaxTokens_ThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new ConversationConfigBuilder();

        // Act
        Action act = () => builder.Build();

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Build_ReturnsRecipeMatchingConfigurationDefaults()
    {
        // Arrange
        const int maxTokens = 75_000;

        // Act
        var config = new ConversationConfigBuilder()
            .WithMaxTokens(maxTokens)
            .Build();

        // Assert
        config.Budget.MaxTokens.Should().Be(maxTokens);
        config.StrategyFactory.Should().NotBeNull();
    }

    private static TestConversationContextFactory CreateFactory() => new();

    private sealed class TestConversationContextFactory : IConversationContextFactory
    {
        private ConversationContextConfiguration _default = new ConversationConfigBuilder()
            .WithMaxTokens(100_000)
            .Build();

        private readonly Dictionary<string, ConversationContextConfiguration> _named = new(StringComparer.Ordinal);

        public IConversationContext Create() =>
            new ConversationContext(_default.Budget, new TokenGuard.Core.TokenCounting.EstimatedTokenCounter(), _default.StrategyFactory(), _default.ObserverFactory());

        public IConversationContext Create(string name)
        {
            if (!_named.TryGetValue(name, out var config))
                throw new InvalidOperationException($"No configuration registered for context name '{name}'.");

            return new ConversationContext(config.Budget, new TokenGuard.Core.TokenCounting.EstimatedTokenCounter(), config.StrategyFactory(), config.ObserverFactory());
        }

        public TestConversationContextFactory AddNamed(string name, ConversationContextConfiguration config)
        {
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(config);
            _named[name] = config;
            return this;
        }

        public TestConversationContextFactory SetDefault(ConversationContextConfiguration config)
        {
            ArgumentNullException.ThrowIfNull(config);
            _default = config;
            return this;
        }
    }

    private static T GetPrivateField<T>(ConversationContext context, string fieldName)
    {
        var field = typeof(ConversationContext).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();

        return (T)field!.GetValue(context)!;
    }

    private sealed class StubCompactionStrategy : ICompactionStrategy
    {
        public Task<CompactionResult> CompactAsync(
            IReadOnlyList<ContextMessage> messages,
            int availableTokens,
            ITokenCounter tokenCounter,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CompactionResult(messages, 0, 0, 0, nameof(StubCompactionStrategy)));
    }

    private sealed class StubCompactionObserver : ICompactionObserver
    {
        public void OnCompaction(CompactionEvent compactionEvent)
        {
        }
    }
}
