using TokenGuard.Core.Options;

namespace TokenGuard.Core.Defaults;

/// <summary>
/// Defines the library's default tiered compaction profile.
/// </summary>
/// <remarks>
/// Tiered compaction currently delegates its concrete tuning knobs to
/// <see cref="SlidingWindowOptions"/> and <see cref="LlmSummarizationOptions"/>. This type keeps a stable
/// namespace anchor for future tiered-specific defaults without forcing the public options contract to change.
/// </remarks>
internal static class TieredCompactionDefaults
{
    /// <summary>
    /// Gets the default two-stage configuration used by <see cref="Strategies.TieredCompactionStrategy"/>.
    /// </summary>
    internal static readonly TieredCompactionOptions Options = new();
}
