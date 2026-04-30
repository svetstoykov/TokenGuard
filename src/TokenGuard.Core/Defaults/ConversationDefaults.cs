using TokenGuard.Core.Configuration;
using TokenGuard.Core.Models;

namespace TokenGuard.Core.Defaults;

/// <summary>
/// Defines the library's default conversation-context profile.
/// </summary>
/// <remarks>
/// The default profile uses 25,000 max tokens, 0.80 compaction, and a 1.0 emergency truncation
/// threshold as a last-resort safety net. Higher-level APIs such as <see cref="ConversationConfigBuilder"/>,
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
    /// Gets the library default emergency truncation threshold.
    /// </summary>
    /// <remarks>
    /// A value of <c>1.0</c> means emergency truncation only fires when the context reaches 100% of
    /// <see cref="MaxTokens"/> — the absolute hard limit. This acts as a last-resort safety net: normal
    /// compaction handles the typical case, and the emergency pass engages only when the primary strategy
    /// cannot bring the context within budget. Disable it by calling
    /// <see cref="Configuration.ConversationConfigBuilder.WithoutEmergencyThreshold"/> on the builder.
    /// </remarks>
    internal const double EmergencyThreshold = 1.0;

    /// <summary>
    /// Gets the library default overrun tolerance as a fraction of the configured maximum token count.
    /// </summary>
    internal const double OverrunTolerance = 0.05;
}
