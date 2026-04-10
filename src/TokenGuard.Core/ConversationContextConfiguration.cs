using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Models;

namespace TokenGuard.Core;

/// <summary>
/// An immutable snapshot of the three parameters that together define the behaviour of a
/// <see cref="ConversationContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ConversationContextConfiguration"/> mirrors the constructor signature of
/// <see cref="ConversationContext"/> exactly. It is produced by
/// <see cref="ConversationContextBuilder.BuildConfiguration"/> and consumed by
/// the built-in factory behind <see cref="Abstractions.IConversationContextFactory"/> to stamp out
/// fresh context instances on demand without re-running the builder defaults logic each time.
/// </para>
/// <para>
/// The record is intentionally minimal — it contains only what is needed to construct a
/// <see cref="ConversationContext"/> and nothing else. Do not add extra properties to it; it is a
/// construction snapshot, not a general-purpose options bag.
/// </para>
/// </remarks>
/// <param name="Budget">
/// Defines the token limits for conversations created from this configuration, including the
/// compaction trigger threshold and the number of tokens reserved for the next model response.
/// </param>
/// <param name="Counter">
/// Counts tokens for individual messages. This should match the target provider as closely as
/// possible so compaction decisions are based on realistic estimates.
/// </param>
/// <param name="Strategy">
/// Produces a smaller message list when the current history no longer fits comfortably within
/// the configured budget.
/// </param>
public sealed record ConversationContextConfiguration(
    ContextBudget Budget,
    ITokenCounter Counter,
    ICompactionStrategy Strategy);
