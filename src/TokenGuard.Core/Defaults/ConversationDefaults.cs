using TokenGuard.Core.Configuration;
using TokenGuard.Core.Models;

namespace TokenGuard.Core.Defaults;

/// <summary>
/// Defines the library's default conversation-context profile.
/// </summary>
/// <remarks>
/// The default profile uses 25,000 max tokens, 0.80 compaction, and no emergency truncation.
/// Higher-level APIs such as <see cref="ConversationConfigBuilder"/>,
/// <see cref="Extensions.ServiceCollectionExtensions"/>, and the built-in
/// <see cref="Abstractions.IConversationContextFactory"/> rely on these values when no custom budget is supplied.
/// </remarks>
internal static class ConversationDefaults
{
    /// <summary>
    /// Gets the library default maximum token budget.
    /// </summary>
    internal const int MaxTokens = 25_000;

    /// <summary>
    /// Gets the library default compaction threshold.
    /// </summary>
    internal const double CompactionThreshold = 0.80;

    /// <summary>
    /// Gets the library default overrun tolerance as a fraction of the configured maximum token count.
    /// </summary>
    internal const double OverrunTolerance = 0.05;
}
