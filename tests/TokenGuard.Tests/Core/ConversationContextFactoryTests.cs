using TokenGuard.Core;
using TokenGuard.Core.Abstractions;

namespace TokenGuard.Tests.Core;

public sealed class ConversationContextFactoryTests
{
    [Fact]
    public void Create_ReturnsNewInstanceOnEachCall()
    {
        IConversationContextFactory factory = CreateFactory();

        using var first = factory.Create();
        using var second = factory.Create();

        Assert.NotSame(first, second);
    }

    [Fact]
    public void Create_ReturnedInstanceIsNotDisposed()
    {
        IConversationContextFactory factory = CreateFactory();

        using var ctx = factory.Create();

        _ = ctx.History;
    }

    [Fact]
    public void CreateNamed_UsesRegisteredConfigurationBudget()
    {
        var config = new ConversationContextBuilder()
            .WithMaxTokens(200_000)
            .BuildConfiguration();

        IConversationContextFactory factory = CreateFactory()
            .AddNamed("large", config);

        using var ctx = factory.Create("large");

        Assert.NotNull(ctx);
        Assert.Empty(ctx.History);
    }

    [Fact]
    public void CreateNamed_ReturnsNewInstanceOnEachCall()
    {
        var config = new ConversationContextBuilder()
            .WithMaxTokens(50_000)
            .BuildConfiguration();

        IConversationContextFactory factory = CreateFactory()
            .AddNamed("small", config);

        using var first = factory.Create("small");
        using var second = factory.Create("small");

        Assert.NotSame(first, second);
    }

    [Fact]
    public void CreateNamed_WithUnregisteredName_ThrowsInvalidOperationException()
    {
        IConversationContextFactory factory = CreateFactory();

        var ex = Assert.Throws<InvalidOperationException>(() => factory.Create("unknown"));

        Assert.Contains("unknown", ex.Message);
    }

    [Fact]
    public void AddNamed_IsFluentAndReturnsSameFactory()
    {
        var config = new ConversationContextBuilder()
            .WithMaxTokens(100_000)
            .BuildConfiguration();

        var factory = CreateFactory();
        var returned = factory.AddNamed("default", config);

        Assert.Same(factory, returned);
    }

    [Fact]
    public void BuildConfiguration_WithoutMaxTokens_ThrowsInvalidOperationException()
    {
        var builder = new ConversationContextBuilder();

        Assert.Throws<InvalidOperationException>(() => builder.BuildConfiguration());
    }

    [Fact]
    public void BuildConfiguration_ReturnsSnapshotMatchingBuildDefaults()
    {
        const int maxTokens = 75_000;

        var config = new ConversationContextBuilder()
            .WithMaxTokens(maxTokens)
            .BuildConfiguration();

        Assert.Equal(maxTokens, config.Budget.MaxTokens);
        Assert.NotNull(config.Counter);
        Assert.NotNull(config.Strategy);
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
