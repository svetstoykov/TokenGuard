namespace TokenGuard.Core.Defaults;

/// <summary>
/// Defines the library's default sliding-window compaction profile.
/// </summary>
/// <remarks>
/// The default profile always preserves at least the newest 10 messages, allows that protected window to grow until it
/// reaches 80% of available tokens, and replaces older tool results with a standardized placeholder string.
/// </remarks>
internal static class SlidingWindowDefaults
{
    /// <summary>
    /// Gets the default minimum number of newest messages that remain unchanged.
    /// </summary>
    internal const int WindowSize = 10;

    /// <summary>
    /// Gets the default fraction of available tokens allocated to the protected newest-message window after the
    /// minimum message floor is satisfied.
    /// </summary>
    internal const double ProtectedWindowFraction = 0.80;

    /// <summary>
    /// Gets the default placeholder format used when masking older tool results.
    /// </summary>
    internal const string PlaceholderFormat = "[Tool result cleared — {0}, {1}]";
}
