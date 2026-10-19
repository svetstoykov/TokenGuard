namespace TokenGuard.Core.Enums;

/// <summary>
/// Controls how aggressively heuristic token counts are biased upward to reduce undercount risk.
/// </summary>
public enum TokenCountSafetyMode
{
    /// <summary>
    /// Returns the raw heuristic estimate without any additional safety margin. The built-in token counter uses this mode.
    /// </summary>
    Balanced,

    /// <summary>
    /// Applies a moderate upward bias of 5%.
    /// </summary>
    Safe,

    /// <summary>
    /// Applies a larger upward bias of 10% when minimizing undercount risk matters more than precision.
    /// </summary>
    Conservative,
}
