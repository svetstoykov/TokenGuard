using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Extensions;

namespace TokenGuard.Tests.Core;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddConversationContext_DefaultOverload_RegistersSingletonFactory()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddConversationContext();

        using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<IConversationContextFactory>();
        var second = provider.GetRequiredService<IConversationContextFactory>();

        // Assert
        first.Should().BeSameAs(second);
    }

    [Fact]
    public void AddConversationContext_ConfiguredDefault_SetsConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddConversationContext(cfg => cfg.WithMaxTokens(150_000));

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IConversationContextFactory>();

        using var context = factory.Create();

        // Assert
        context.Should().NotBeNull();
    }

    [Fact]
    public void AddConversationContext_NamedOverload_AfterConfiguredDefault_NamedProfileIsAccessible()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddConversationContext(cfg => cfg.WithMaxTokens(150_000));
        services.AddConversationContext("large", cfg => cfg.WithMaxTokens(200_000));

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IConversationContextFactory>();

        using var context = factory.Create("large");

        // Assert
        context.Should().NotBeNull();
        context.History.Should().BeEmpty();
    }

    [Fact]
    public void AddConversationContext_NamedOverload_RegistersNamedConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddConversationContext("large", cfg => cfg.WithMaxTokens(200_000));

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IConversationContextFactory>();

        using var context = factory.Create("large");

        // Assert
        context.Should().NotBeNull();
        context.History.Should().BeEmpty();
    }

    [Fact]
    public void AddConversationContext_RepeatedNamedRegistrations_DoNotDuplicateSingletonFactory()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddConversationContext();
        services.AddConversationContext("large", cfg => cfg.WithMaxTokens(200_000));
        services.AddConversationContext("small", cfg => cfg.WithMaxTokens(50_000));

        var factoryRegistrations = services.Count(descriptor =>
            descriptor.ServiceType == typeof(IConversationContextFactory));

        // Assert
        factoryRegistrations.Should().Be(1);
    }

    [Fact]
    public void AddConversationContext_NamedOnlyRegistrations_DoNotDuplicateSingletonFactory()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddConversationContext("large", cfg => cfg.WithMaxTokens(200_000));
        services.AddConversationContext("small", cfg => cfg.WithMaxTokens(50_000));

        var factoryRegistrations = services.Count(descriptor =>
            descriptor.ServiceType == typeof(IConversationContextFactory));

        // Assert
        factoryRegistrations.Should().Be(1);
    }

    [Fact]
    public void AddConversationContext_ReusingNamedRegistration_ReplacesExistingProfile()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddConversationContext("default", cfg => cfg.WithMaxTokens(120_000));
        services.AddConversationContext("default", cfg => cfg.WithMaxTokens(80_000));

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IConversationContextFactory>();

        using var context = factory.Create("default");

        // Assert
        context.Should().NotBeNull();
    }

    [Fact]
    public void AddConversationContext_CalledTwice_Throws()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddConversationContext();

        // Act
        var act = () => services.AddConversationContext();

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddConversationContext_ConfiguredCalledTwice_Throws()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddConversationContext(cfg => cfg.WithMaxTokens(50_000));

        // Act
        var act = () => services.AddConversationContext(cfg => cfg.WithMaxTokens(80_000));

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddConversationContext_CalledAfterNamedRegistration_Throws()
    {
        // Arrange — named overload implicitly creates a default factory
        var services = new ServiceCollection();
        services.AddConversationContext("large", cfg => cfg.WithMaxTokens(200_000));

        // Act
        var act = () => services.AddConversationContext();

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddConversationContext_ConfiguredCalledAfterNamedRegistration_Throws()
    {
        // Arrange — named overload implicitly creates a default factory
        var services = new ServiceCollection();
        services.AddConversationContext("large", cfg => cfg.WithMaxTokens(200_000));

        // Act
        var act = () => services.AddConversationContext(cfg => cfg.WithMaxTokens(50_000));

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddConversationContext_NamedOnly_ImplicitDefaultCreatesUsableContext()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act — only a named profile is registered; default factory is created implicitly
        services.AddConversationContext("large", cfg => cfg.WithMaxTokens(200_000));

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IConversationContextFactory>();

        using var context = factory.Create();

        // Assert — implicit default context is usable
        context.Should().NotBeNull();
        context.History.Should().BeEmpty();
    }
}
