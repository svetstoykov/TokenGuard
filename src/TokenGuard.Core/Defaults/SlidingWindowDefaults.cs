namespace TokenGuard.Core.Defaults;

/// <summary>
/// Defines the library's default sliding-window compaction profile.
/// </summary>
/// <remarks>
/// The default profile always preserves at least the newest 10 messages, allows that protected window to grow until it
/// reaches 80% of available tokens, moves the masking boundary in steps of a quarter of that allowance, and replaces
/// older tool results with a standardized placeholder string.
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
    /// Gets the share of the protected-window allowance by which the masking boundary moves at a time.
    /// </summary>
    internal const double MaskingStepFraction = 0.25;

    /// <summary>
    /// Gets the default placeholder format used when masking older tool results.
    /// </summary>
    internal const string PlaceholderFormat = "[Tool result removed to save context — {0}, {1}. Call the tool again if you need this content.]";
}
