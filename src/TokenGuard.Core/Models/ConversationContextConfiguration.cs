using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Configuration;

namespace TokenGuard.Core.Models;

/// <summary>
/// An immutable construction recipe for the built-in token counter, the compaction services, and the budget that define the behaviour of a
/// <see cref="ConversationContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ConversationContextConfiguration"/> is produced by <see cref="ConversationConfigBuilder.Build"/>
/// and consumed by the built-in factory behind <see cref="Abstractions.IConversationContextFactory"/> to stamp out
/// fresh context instances on demand without re-running the builder defaults logic each time.
/// </para>
/// <para>
/// TokenGuard's built-in <see cref="ITokenCounter"/> implementation is constructed exactly once per
/// <see cref="Abstractions.IConversationContextFactory.Create()"/> or
/// <see cref="Abstractions.IConversationContextFactory.Create(string)"/> call.
/// <paramref name="StrategyFactory"/> and <paramref name="ObserverFactory"/> are also each invoked exactly once per
/// factory call. No object produced for one context lifetime is shared with another context created through the
/// built-in factory.
/// </para>
/// <para>
/// The record is intentionally minimal — it contains only what is needed to construct a
/// <see cref="ConversationContext"/> and nothing else. Do not add extra properties to it; it is a
/// construction recipe, not a general-purpose options bag.
/// </para>
/// </remarks>
/// <param name="Budget">
/// Defines the token limits for conversations created from this configuration, including the
/// compaction trigger threshold and the number of tokens reserved for the next model response.
/// </param>
/// <param name="StrategyFactory">
/// Creates the <see cref="ICompactionStrategy"/> used by one conversation-context instance. The built-in factory
/// invokes this delegate once for each created context so compaction state is never shared across context lifetimes.
/// </param>
/// <param name="ObserverFactory">
/// Creates the optional <see cref="ICompactionObserver"/> used by one conversation-context instance. The built-in
/// factory invokes this delegate once for each created context; when the delegate returns <see langword="null"/>,
/// that context emits no compaction notifications.
/// </param>
public sealed record ConversationContextConfiguration(
    ContextBudget Budget,
    Func<ICompactionStrategy> StrategyFactory,
    Func<ICompactionObserver?> ObserverFactory);
