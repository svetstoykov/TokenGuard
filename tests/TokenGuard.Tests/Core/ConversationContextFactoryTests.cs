using FluentAssertions;
using System.Reflection;
using TokenGuard.Core;
using TokenGuard.Core.Abstractions;
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
    public void Create_InvokesFreshDependenciesOnEachCall()
    {
        // Arrange
        var strategyCalls = 0;
        var factory = new ConversationContextFactory(new ConversationContextConfiguration(
            ContextBudget.For(100_000),
            _ =>
            {
                strategyCalls++;
                return new StubCompactionStrategy();
            }));

        // Act
        using var first = (ConversationContext)factory.Create();
        using var second = (ConversationContext)factory.Create();
        var firstCounter = GetPrivateField<ITokenCounter>(first, "_counter");
        var secondCounter = GetPrivateField<ITokenCounter>(second, "_counter");

        // Assert
        strategyCalls.Should().Be(2);
        firstCounter.Should().NotBeSameAs(secondCounter);
        GetPrivateField<ICompactionStrategy>(first, "_strategy").Should().NotBeSameAs(GetPrivateField<ICompactionStrategy>(second, "_strategy"));
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
            CreateContext(_default);

        public IConversationContext Create(string name)
        {
            if (!_named.TryGetValue(name, out var config))
                throw new InvalidOperationException($"No configuration registered for context name '{name}'.");

            return CreateContext(config);
        }

        public TestConversationContextFactory AddNamed(string name, ConversationContextConfiguration config)
        {
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(config);
            _named[name] = config;
            return this;
        }

        private static ConversationContext CreateContext(ConversationContextConfiguration config)
        {
            var counter = new TokenGuard.Core.TokenCounting.EstimatedTokenCounter();
            return new ConversationContext(config.Budget, counter, config.StrategyFactory(counter));
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
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CompactionResult(messages, 0, 0, 0, nameof(StubCompactionStrategy)));
    }
}
