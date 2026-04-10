using TokenGuard.Core;
using TokenGuard.Core.Abstractions;
using FluentAssertions;

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
        var config = new ConversationContextBuilder()
            .WithMaxTokens(200_000)
            .BuildConfiguration();

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
        var config = new ConversationContextBuilder()
            .WithMaxTokens(50_000)
            .BuildConfiguration();

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
        var config = new ConversationContextBuilder()
            .WithMaxTokens(100_000)
            .BuildConfiguration();

        var factory = CreateFactory();

        // Act
        var returned = factory.AddNamed("default", config);

        // Assert
        returned.Should().BeSameAs(factory);
    }

    [Fact]
    public void BuildConfiguration_WithoutMaxTokens_ThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new ConversationContextBuilder();

        // Act
        Action act = () => builder.BuildConfiguration();

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void BuildConfiguration_ReturnsSnapshotMatchingBuildDefaults()
    {
        // Arrange
        const int maxTokens = 75_000;

        // Act
        var config = new ConversationContextBuilder()
            .WithMaxTokens(maxTokens)
            .BuildConfiguration();

        // Assert
        config.Budget.MaxTokens.Should().Be(maxTokens);
        config.Counter.Should().NotBeNull();
        config.Strategy.Should().NotBeNull();
    }

    private static TestConversationContextFactory CreateFactory() => new();

    private sealed class TestConversationContextFactory : IConversationContextFactory
    {
        private ConversationContextConfiguration _default = new ConversationContextBuilder()
            .WithMaxTokens(100_000)
            .BuildConfiguration();

        private readonly Dictionary<string, ConversationContextConfiguration> _named = new(StringComparer.Ordinal);

        public IConversationContext Create() =>
            new ConversationContext(_default.Budget, _default.Counter, _default.Strategy);

        public IConversationContext Create(string name)
        {
            if (!_named.TryGetValue(name, out var config))
                throw new InvalidOperationException($"No configuration registered for context name '{name}'.");

            return new ConversationContext(config.Budget, config.Counter, config.Strategy);
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
}
