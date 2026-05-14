namespace TokenGuard.Core.Enums;

/// <summary>
/// Controls how aggressively heuristic token counts are biased upward to reduce undercount risk.
/// </summary>
public enum TokenCountSafetyMode
{
    /// <summary>
    /// Returns the raw heuristic estimate without any additional safety margin.
    /// </summary>
    Balanced,

    /// <summary>
    /// Applies a moderate upward bias suitable for TokenGuard's default protection behavior.
    /// </summary>
    Safe,

    /// <summary>
    /// Applies a larger upward bias when minimizing undercount risk matters more than precision.
    /// </summary>
    Conservative,
}
