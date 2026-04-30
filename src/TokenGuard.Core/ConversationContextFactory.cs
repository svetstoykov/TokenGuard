using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Configuration;
using TokenGuard.Core.Models;
using TokenGuard.Core.TokenCounting;

namespace TokenGuard.Core;

/// <summary>
/// Creates fresh <see cref="IConversationContext"/> instances from one default configuration and optional named profiles.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ConversationContextFactory"/> is TokenGuard's manual-construction entry point for applications that do
/// not use dependency injection. Supply a default <see cref="ConversationContextConfiguration"/> at construction time,
/// then call <see cref="Create()"/> to obtain a new conversation state object for each session.
/// </para>
/// <para>
/// The factory remains mutable only for named profiles through <see cref="AddNamed(string, ConversationContextConfiguration)"/>.
/// The default profile is supplied through the constructor, and each create call still constructs a fresh token counter
/// and strategy instance so no per-conversation state is shared across calls.
/// </para>
/// <para>
/// Dependency injection remains the strongly encouraged path. When a container is available, prefer
/// <c>AddConversationContext(...)</c> and consume <see cref="IConversationContextFactory"/> instead of constructing
/// this type manually.
/// </para>
/// </remarks>
public sealed class ConversationContextFactory : IConversationContextFactory
{
    private readonly ConversationContextConfiguration _default;
    private readonly Dictionary<string, ConversationContextConfiguration> _named = new(StringComparer.Ordinal);

    /// <summary>
    /// Initialises a new <see cref="ConversationContextFactory"/> with the supplied default configuration.
    /// </summary>
    /// <param name="config">
    /// The immutable configuration recipe used for <see cref="Create()"/> calls.
    /// </param>
    /// <remarks>
    /// The default configuration is identical to the library default profile when <paramref name="config"/> comes from
    /// <see cref="ConversationConfigBuilder.Default(int)"/>. Additional named profiles can be attached later with
    /// <see cref="AddNamed(string, ConversationContextConfiguration)"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="config"/> is <see langword="null"/>.</exception>
    public ConversationContextFactory(ConversationContextConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        this._default = config;
    }

    /// <summary>
    /// Registers a named configuration that can later be retrieved via <see cref="Create(string)"/>.
    /// </summary>
    /// <param name="name">
    /// The name to associate with the configuration. Names are compared using ordinal string
    /// comparison. Registering a name more than once replaces the previous entry.
    /// </param>
    /// <param name="config">
    /// The immutable configuration recipe to store. Typically produced by
    /// <see cref="ConversationConfigBuilder.Build"/>.
    /// </param>
    /// <returns>This factory instance, enabling fluent chaining of multiple <see cref="AddNamed"/> calls.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="name"/> or <paramref name="config"/> is null.
    /// </exception>
    public ConversationContextFactory AddNamed(string name, ConversationContextConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(config);
        this._named[name] = config;
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
    /// produced by this factory. A fresh built-in token counter is constructed for every call, and the
    /// configured strategy delegate is invoked for every call, so no produced dependency instance is
    /// reused across contexts.
    /// </remarks>
    public IConversationContext Create() => CreateContext(this._default);

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
    /// produced by this factory, even when the same name is supplied repeatedly. A fresh built-in
    /// token counter is constructed for every call, and the configured strategy delegate is invoked for
    /// every call, so no produced dependency instance is reused across contexts.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no configuration has been registered under <paramref name="name"/>. The exception
    /// message includes the requested name to aid diagnosis.
    /// </exception>
    public IConversationContext Create(string name)
    {
        if (!this._named.TryGetValue(name, out var config))
            throw new InvalidOperationException($"No configuration registered for context name '{name}'.");

        return CreateContext(config);
    }

    internal IEnumerable<KeyValuePair<string, ConversationContextConfiguration>> NamedConfigurations => this._named;

    private static ConversationContext CreateContext(ConversationContextConfiguration config)
    {
        var counter = new EstimatedTokenCounter();
        var strategy = config.StrategyFactory(counter);

        return new ConversationContext(config.Budget, counter, strategy);
    }
}
