using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Configuration;
using TokenGuard.Core.Models;

namespace TokenGuard.Core.Extensions;

/// <summary>
/// Registers <see cref="IConversationContextFactory"/> and its conversation-context configurations
/// with a dependency-injection container.
/// </summary>
/// <remarks>
/// <para>
/// These extensions are the supported dependency-injection entry point for conversation-context
/// creation. Call one of the default-registration overloads exactly once per service collection,
/// then add any number of named profiles with
/// <see cref="ServiceCollectionExtensions.AddConversationContext(IServiceCollection, string, Action{ConversationConfigBuilder})"/>.
/// Calling a default-registration overload more than once throws <see cref="InvalidOperationException"/>.
/// </para>
/// <para>
/// Each registration callback receives a fresh <see cref="ConversationConfigBuilder"/>. The builder
/// state is captured immediately as an immutable <see cref="ConversationContextConfiguration"/> recipe,
/// so later service-resolution paths do not depend on mutable startup objects while each
/// <see cref="IConversationContextFactory.Create()"/> call still receives freshly constructed dependencies.
/// </para>
/// <para>
/// Applications that do not use dependency injection can still construct
/// <see cref="ConversationContextFactory"/> manually. These registration methods remain the preferred
/// path when a container is available because they keep all factory configuration in one startup flow
/// while exposing only <see cref="IConversationContextFactory"/> to consuming services.
/// </para>
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the conversation-context factory using the library's default configuration.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance for fluent chaining.</returns>
    /// <remarks>
    /// Registers <see cref="IConversationContextFactory"/> as a singleton using the library default
    /// profile: 25,000 max tokens, 0.80 compaction, 1.0 emergency truncation as a last-resort safety net,
    /// 0 reserved tokens, TokenGuard's built-in heuristic <see cref="ITokenCounter"/> implementation, and
    /// <see cref="Strategies.SlidingWindowStrategy"/>.
    /// For non-DI scenarios, construct <see cref="ConversationContextFactory"/> directly instead.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a conversation-context factory has already been registered on
    /// <paramref name="services"/>. Call this overload at most once per service collection.
    /// To add named profiles after the default registration, use
    /// <see cref="AddConversationContext(IServiceCollection, string, Action{ConversationConfigBuilder})"/>.
    /// </exception>
    public static IServiceCollection AddConversationContext(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        ThrowIfFactoryAlreadyRegistered(services);

        RegisterFactory(services, new ConversationContextFactory(ConversationConfigBuilder.Default()));

        return services;
    }

    /// <summary>
    /// Registers the conversation-context factory with a custom default configuration.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <param name="configure">
    /// A callback that configures a fresh <see cref="ConversationConfigBuilder"/> for unnamed
    /// <see cref="IConversationContextFactory.Create()"/> calls.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> instance for fluent chaining.</returns>
    /// <remarks>
    /// The <see cref="IConversationContextFactory"/> abstraction is registered once as a singleton
    /// pointing to a <see cref="ConversationContextFactory"/> built from the supplied recipe. Named
    /// profiles can be added afterward using
    /// <see cref="AddConversationContext(IServiceCollection, string, Action{ConversationConfigBuilder})"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="configure"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a conversation-context factory has already been registered on
    /// <paramref name="services"/>. Call this overload at most once per service collection.
    /// </exception>
    public static IServiceCollection AddConversationContext(
        this IServiceCollection services,
        Action<ConversationConfigBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        ThrowIfFactoryAlreadyRegistered(services);

        var builder = new ConversationConfigBuilder();
        configure(builder);
        RegisterFactory(services, new ConversationContextFactory(builder.Build()));

        return services;
    }

    /// <summary>
    /// Registers a named conversation-context profile alongside the singleton factory.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <param name="name">
    /// The profile name to associate with the configured recipe. Names are compared using ordinal
    /// string comparison when later resolved through <see cref="IConversationContextFactory.Create(string)"/>.
    /// </param>
    /// <param name="configure">
    /// A callback that configures a fresh <see cref="ConversationConfigBuilder"/> for the named
    /// profile.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> instance for fluent chaining.</returns>
    /// <remarks>
    /// If no factory is registered yet, this overload creates a default factory using the library's
    /// default profile before adding the named configuration. Re-registering the same
    /// <paramref name="name"/> replaces the previous named recipe.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/>, <paramref name="name"/>, or <paramref name="configure"/>
    /// is <see langword="null"/>.
    /// </exception>
    public static IServiceCollection AddConversationContext(
        this IServiceCollection services,
        string name,
        Action<ConversationConfigBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(configure);

        EnsureDefaultFactoryRegistered(services);

        var factory = services.GetConversationContextFactory();
        var builder = new ConversationConfigBuilder();
        configure(builder);
        factory.AddNamed(name, builder.Build());

        return services;
    }

    private static void ThrowIfFactoryAlreadyRegistered(IServiceCollection services)
    {
        if (services.GetRegisteredConversationContextFactory() is not null)
        {
            throw new InvalidOperationException(
                $"{nameof(AddConversationContext)} has already been called. " +
                "Register the conversation-context factory at most once per service collection.");
        }
    }

    private static void EnsureDefaultFactoryRegistered(IServiceCollection services)
    {
        if (services.GetRegisteredConversationContextFactory() is not null)
            return;

        RegisterFactory(services, new ConversationContextFactory(ConversationConfigBuilder.Default()));
    }

    private static void RegisterFactory(IServiceCollection services, ConversationContextFactory factory)
    {
        services.AddSingleton(factory);
        services.TryAddSingleton<IConversationContextFactory>(static sp =>
            sp.GetRequiredService<ConversationContextFactory>());
    }

    private static ConversationContextFactory GetConversationContextFactory(this IServiceCollection services)
    {
        return services.GetRegisteredConversationContextFactory()
            ?? throw new InvalidOperationException(
                $"{nameof(ConversationContextFactory)} must be registered before configuration is applied.");
    }

    private static ConversationContextFactory? GetRegisteredConversationContextFactory(this IServiceCollection services)
    {
        return services
            .Select(static descriptor => descriptor.ImplementationInstance)
            .OfType<ConversationContextFactory>()
            .SingleOrDefault();
    }
}
