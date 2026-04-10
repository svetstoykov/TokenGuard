using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Models;

namespace TokenGuard.Core;

internal sealed class ConversationContextFactory : IConversationContextFactory
{
    private ConversationContextConfiguration _default;
    private readonly Dictionary<string, ConversationContextConfiguration> _named = new(StringComparer.Ordinal);

    /// <summary>
    /// Initialises the factory with the standard default configuration: a 100,000-token budget,
    /// 0.80 compaction threshold, 0.95 emergency threshold, 0 reserved tokens,
    /// <see cref="TokenCounting.EstimatedTokenCounter"/>, and <see cref="Strategies.SlidingWindowStrategy"/>.
    /// </summary>
    /// <remarks>
    /// The default configuration is identical to the library default profile produced by
    /// <c>new ConversationConfigBuilder().WithMaxTokens(100_000).Build()</c>.
    /// It can be overridden per call site by registering a named configuration with
    /// <see cref="AddNamed"/> and calling <see cref="Create(string)"/> instead.
    /// </remarks>
    internal ConversationContextFactory(ConversationContextConfiguration config)
    {
        _default = config;
    }

    /// <summary>
    /// Replaces the default configuration used by <see cref="Create()"/>.
    /// </summary>
    /// <param name="config">
    /// The immutable configuration snapshot to use for unnamed context creation.
    /// </param>
    /// <returns>
    /// This factory instance, enabling fluent startup configuration alongside
    /// <see cref="AddNamed(string, ConversationContextConfiguration)"/>.
    /// </returns>
    /// <remarks>
    /// This method exists primarily for dependency-injection registration flows where repeated
    /// calls to a service-collection extension should be able to configure the singleton factory
    /// without replacing the singleton registration itself.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="config"/> is null.
    /// </exception>
    internal ConversationContextFactory SetDefault(ConversationContextConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _default = config;
        return this;
    }

    /// <summary>
    /// Registers a named configuration that can later be retrieved via <see cref="Create(string)"/>.
    /// </summary>
    /// <param name="name">
    /// The name to associate with the configuration. Names are compared using ordinal string
    /// comparison. Registering a name more than once replaces the previous entry.
    /// </param>
    /// <param name="config">
    /// The immutable configuration snapshot to store. Typically produced by
    /// <see cref="ConversationConfigBuilder.Build"/>.
    /// </param>
    /// <returns>This factory instance, enabling fluent chaining of multiple <see cref="AddNamed"/> calls.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="name"/> or <paramref name="config"/> is null.
    /// </exception>
    internal ConversationContextFactory AddNamed(string name, ConversationContextConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(config);
        _named[name] = config;
        return this;
    }

    /// <summary>
    /// Creates a new <see cref="ConversationContext"/> using the default configuration.
    /// </summary>
    /// <returns>
    /// A fresh, independent context instance. The caller is responsible for disposing it when the
    /// conversation ends.
    /// </returns>
    /// <remarks>
    /// Each call returns a distinct instance that shares no history or state with any other context
    /// produced by this factory.
    /// </remarks>
    public IConversationContext Create() =>
        new ConversationContext(_default.Budget, _default.Counter, _default.Strategy);

    /// <summary>
    /// Creates a new <see cref="ConversationContext"/> using a previously registered named configuration.
    /// </summary>
    /// <param name="name">
    /// The name used when the configuration was registered via <see cref="AddNamed"/>. Names are
    /// compared using ordinal string comparison.
    /// </param>
    /// <returns>
    /// A fresh, independent context instance built from the named configuration. The caller is
    /// responsible for disposing it when the conversation ends.
    /// </returns>
    /// <remarks>
    /// Each call returns a distinct instance that shares no history or state with any other context
    /// produced by this factory, even when the same name is supplied repeatedly.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no configuration has been registered under <paramref name="name"/>. The exception
    /// message includes the requested name to aid diagnosis.
    /// </exception>
    public IConversationContext Create(string name)
    {
        if (!_named.TryGetValue(name, out var config))
            throw new InvalidOperationException($"No configuration registered for context name '{name}'.");

        return new ConversationContext(config.Budget, config.Counter, config.Strategy);
    }
}
