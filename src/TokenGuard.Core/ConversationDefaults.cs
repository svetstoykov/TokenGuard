using TokenGuard.Core.Models;

namespace TokenGuard.Core;

/// <summary>
/// Defines the library's default conversation-context profile.
/// </summary>
/// <remarks>
/// The default profile uses 100,000 max tokens, 0.80 compaction, 0.95 emergency, and 0 reserved tokens.
/// Higher-level APIs such as <see cref="ConversationConfigBuilder"/>,
/// <see cref="Extensions.ServiceCollectionExtensions"/>, and the built-in
/// <see cref="Abstractions.IConversationContextFactory"/> rely on these values when no custom budget is supplied.
/// </remarks>
internal static class ConversationDefaults
{
    /// <summary>
    /// Gets the library default maximum token budget.
    /// </summary>
    internal const int MaxTokens = 100_000;

    /// <summary>
    /// Gets the library default compaction threshold.
    /// </summary>
    internal const double CompactionThreshold = 0.80;

    /// <summary>
    /// Gets the library default emergency threshold.
    /// </summary>
    internal const double EmergencyThreshold = 0.95;

    /// <summary>
    /// Gets the library default reserved token count.
    /// </summary>
    internal const int ReservedTokens = 0;

    /// <summary>
    /// Creates the library default <see cref="ContextBudget"/> for a specific maximum token count.
    /// </summary>
    /// <param name="maxTokens">The total token capacity of the target model context window.</param>
    /// <returns>A <see cref="ContextBudget"/> using the library default thresholds and reserved tokens.</returns>
    internal static ContextBudget CreateBudget(int maxTokens)
    {
        return new ContextBudget(maxTokens);
    }
}
