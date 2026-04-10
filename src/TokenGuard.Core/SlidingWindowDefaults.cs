namespace TokenGuard.Core;

/// <summary>
/// Defines the library's default sliding-window compaction profile.
/// </summary>
/// <remarks>
/// The default profile preserves the newest 10 messages, reserves 40% of available tokens for that protected window,
/// and replaces older tool results with a standardized placeholder string.
/// </remarks>
internal static class SlidingWindowDefaults
{
    /// <summary>
    /// Gets the default number of newest messages that remain unchanged.
    /// </summary>
    internal const int WindowSize = 10;

    /// <summary>
    /// Gets the default fraction of available tokens reserved for the protected newest-message window.
    /// </summary>
    internal const double ProtectedWindowFraction = 0.40;

    /// <summary>
    /// Gets the default placeholder format used when masking older tool results.
    /// </summary>
    internal const string PlaceholderFormat = "[Tool result cleared — {0}, {1}]";
}
