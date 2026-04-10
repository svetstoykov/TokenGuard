using Microsoft.Extensions.DependencyInjection;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Extensions;

namespace TokenGuard.Tests.Core;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddConversationContext_DefaultOverload_RegistersSingletonFactory()
    {
        var services = new ServiceCollection();

        services.AddConversationContext();

        using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<IConversationContextFactory>();
        var second = provider.GetRequiredService<IConversationContextFactory>();

        Assert.Same(first, second);
    }

    [Fact]
    public void AddConversationContext_ConfiguredDefault_ReplacesUnnamedConfiguration()
    {
        var services = new ServiceCollection();

        services.AddConversationContext(cfg => cfg.WithMaxTokens(150_000));

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IConversationContextFactory>();

        using var context = factory.Create();

        Assert.NotNull(context);
    }

    [Fact]
    public void AddConversationContext_NamedOverload_RegistersNamedConfiguration()
    {
        var services = new ServiceCollection();

        services.AddConversationContext("large", cfg => cfg.WithMaxTokens(200_000));

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IConversationContextFactory>();

        using var context = factory.Create("large");

        Assert.NotNull(context);
        Assert.Empty(context.History);
    }

    [Fact]
    public void AddConversationContext_RepeatedRegistrations_DoNotDuplicateSingletonFactory()
    {
        var services = new ServiceCollection();

        services.AddConversationContext();
        services.AddConversationContext("large", cfg => cfg.WithMaxTokens(200_000));
        services.AddConversationContext("small", cfg => cfg.WithMaxTokens(50_000));

        var factoryRegistrations = services.Count(descriptor =>
            descriptor.ServiceType == typeof(IConversationContextFactory));

        Assert.Equal(1, factoryRegistrations);
    }

    [Fact]
    public void AddConversationContext_ReusingNamedRegistration_ReplacesExistingProfile()
    {
        var services = new ServiceCollection();

        services.AddConversationContext("default", cfg => cfg.WithMaxTokens(120_000));
        services.AddConversationContext("default", cfg => cfg.WithMaxTokens(80_000));

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IConversationContextFactory>();

        using var context = factory.Create("default");

        Assert.NotNull(context);
    }
}
