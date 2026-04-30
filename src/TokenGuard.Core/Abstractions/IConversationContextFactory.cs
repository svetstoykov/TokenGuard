namespace TokenGuard.Core.Abstractions;

/// <summary>
/// Creates isolated <see cref="IConversationContext"/> instances on demand.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IConversationContextFactory"/> is the preferred entry point for obtaining conversation
/// contexts in dependency-injected applications. Register it through the
/// <c>AddConversationContext(...)</c> service-collection extensions and inject it wherever a new
/// conversation needs to start. Each call to <see cref="Create()"/> or <see cref="Create(string)"/>
/// returns a completely independent context that shares no state with any previously created
/// instance.
/// </para>
/// <para>
/// The factory pattern is specifically designed to address the singleton misuse problem: a
/// <see cref="IConversationContext"/> must never be shared across concurrent requests or reused
/// between conversations, because its history grows for the lifetime of the object. The factory
/// keeps the configuration singleton while ensuring the stateful context is freshly allocated every
/// time it is needed.
/// </para>
/// <para>
/// Applications that do not use a container can construct <see cref="ConversationContextFactory"/>
/// directly and then create contexts through it. The same per-conversation lifetime rule applies:
/// each call must produce a fresh context instance rather than sharing one across sessions.
/// </para>
/// <para>
/// Named configurations allow multiple distinct context profiles — for example, a large-window
/// context for document analysis alongside a compact one for short question-answering — to coexist
/// in the same factory. Each profile is registered once at startup and can be retrieved by name at
/// any call site.
/// </para>
/// </remarks>
public interface IConversationContextFactory
{
    /// <summary>
    /// Creates a new context using the default configuration.
    /// </summary>
    /// <returns>
    /// A fresh, independent <see cref="IConversationContext"/> instance. The caller is responsible
    /// for disposing it when the conversation ends.
    /// </returns>
    /// <remarks>
    /// Every call produces a distinct instance. Contexts returned by this method are never reused
    /// or shared with other callers. Unless startup registration replaced it, the library default profile
    /// uses 25,000 max tokens, 0.80 compaction, no emergency truncation, 0 reserved tokens,
    /// TokenGuard's built-in heuristic <see cref="ITokenCounter"/> implementation, and
    /// <see cref="Strategies.SlidingWindowStrategy"/>.
    /// </remarks>
    IConversationContext Create();

    /// <summary>
    /// Creates a new context using a previously registered named configuration.
    /// </summary>
    /// <param name="name">
    /// The name used when the configuration was registered. Names are compared using ordinal
    /// string comparison.
    /// </param>
    /// <returns>
    /// A fresh, independent <see cref="IConversationContext"/> instance built from the named
    /// configuration. The caller is responsible for disposing it when the conversation ends.
    /// </returns>
    /// <remarks>
    /// Every call produces a distinct instance even when the same name is supplied repeatedly.
    /// Contexts returned by this method are never reused or shared with other callers.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no configuration has been registered under <paramref name="name"/>.
    /// </exception>
    IConversationContext Create(string name);
}
