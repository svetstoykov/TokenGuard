using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Models;

namespace TokenGuard.Core.Extensions;

/// <summary>
/// Registers <see cref="IConversationContextFactory"/> and its conversation-context configurations
/// with a dependency-injection container.
/// </summary>
/// <remarks>
/// <para>
/// These extensions are the supported dependency-injection entry point for conversation-context
/// creation. They register the built-in factory once and then allow later calls to update the
/// unnamed default profile or add additional named profiles without duplicating the singleton
/// registration.
/// </para>
/// <para>
/// Each registration callback receives a fresh <see cref="ConversationConfigBuilder"/>. The builder
/// state is captured immediately as an immutable <see cref="ConversationContextConfiguration"/>, so
/// later service-resolution paths do not depend on mutable startup objects.
/// </para>
/// <para>
/// Applications that do not use dependency injection can still construct <see cref="ConversationContext"/>
/// directly. The built-in factory implementation itself remains internal so the supported container
/// setup stays centered on these registration methods and <see cref="IConversationContextFactory"/>.
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
    /// This overload ensures <see cref="IConversationContextFactory"/> is available as a singleton
    /// while preserving any previously registered named profiles. The library default profile uses
    /// 100,000 max tokens, 0.80 compaction, 0.95 emergency, 0 reserved tokens,
    /// <see cref="TokenCounting.EstimatedTokenCounter"/>, and <see cref="Strategies.SlidingWindowStrategy"/>.
    /// For non-DI scenarios, construct <see cref="ConversationContext"/> directly instead of registering
    /// custom factory instances.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> is null.
    /// </exception>
    public static IServiceCollection AddConversationContext(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var existingFactory = services.GetRegisteredConversationContextFactory();
        if (existingFactory is null)
        {
            var defaultConfig = ConversationConfigBuilder.Default();
            var factory = new ConversationContextFactory(defaultConfig);
            services.AddSingleton(factory);
        }

        services.TryAddSingleton<IConversationContextFactory>(static serviceProvider =>
            serviceProvider.GetRequiredService<ConversationContextFactory>());

        return services;
    }

    /// <summary>
    /// Registers the conversation-context factory and replaces the unnamed default configuration.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <param name="configure">
    /// A callback that configures a fresh <see cref="ConversationConfigBuilder"/> for unnamed
    /// <see cref="IConversationContextFactory.Create()"/> calls.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> instance for fluent chaining.</returns>
    /// <remarks>
    /// Repeated calls remain idempotent with respect to singleton registration: the factory is
    /// registered only once, and each call updates the default configuration snapshot stored by that
    /// singleton. This keeps application startup on a single supported registration path while still
    /// allowing direct <see cref="ConversationContext"/> construction outside the container.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="configure"/> is null.
    /// </exception>
    public static IServiceCollection AddConversationContext(
        this IServiceCollection services,
        Action<ConversationConfigBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddConversationContext();

        var factory = services.GetConversationContextFactory();
        var builder = new ConversationConfigBuilder();
        configure(builder);
        factory.SetDefault(builder.Build());

        return services;
    }

    /// <summary>
    /// Registers a named conversation-context profile alongside the singleton factory.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <param name="name">
    /// The profile name to associate with the configured snapshot. Names are compared using ordinal
    /// string comparison when later resolved through <see cref="IConversationContextFactory.Create(string)"/>.
    /// </param>
    /// <param name="configure">
    /// A callback that configures a fresh <see cref="ConversationConfigBuilder"/> for the named
    /// profile.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> instance for fluent chaining.</returns>
    /// <remarks>
    /// Re-registering the same <paramref name="name"/> replaces the previous named snapshot. This
    /// allows a single container registration flow to manage all named profiles exposed through
    /// <see cref="IConversationContextFactory.Create(string)"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/>, <paramref name="name"/>, or <paramref name="configure"/>
    /// is null.
    /// </exception>
    public static IServiceCollection AddConversationContext(
        this IServiceCollection services,
        string name,
        Action<ConversationConfigBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddConversationContext();

        var factory = services.GetConversationContextFactory();
        var builder = new ConversationConfigBuilder();
        configure(builder);
        factory.AddNamed(name, builder.Build());

        return services;
    }

    private static ConversationContextFactory GetConversationContextFactory(this IServiceCollection services)
    {
        var factory = services.GetRegisteredConversationContextFactory();

        return factory ?? throw new InvalidOperationException(
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
