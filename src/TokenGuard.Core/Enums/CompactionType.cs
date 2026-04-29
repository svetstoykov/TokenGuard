namespace TokenGuard.Core.Enums;

/// <summary>
/// Identifies what kind of compaction effect was applied during a compaction cycle.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CompactionType"/> describes the observable reduction behavior represented by a
/// <see cref="Models.CompactionResult"/>. It is separate from <see cref="CompactionTrigger"/>, which explains why the
/// cycle ran, and from <see cref="Models.CompactionResult.StrategyName"/>, which identifies which strategy produced the
/// result.
/// </para>
/// <para>
/// Combined values that include emergency truncation are used only when a masking or pass-through strategy result is
/// followed by an oldest-first emergency drop pass inside <see cref="ConversationContext"/>.
/// </para>
/// </remarks>
public enum CompactionType
{
    /// <summary>
    /// No compaction effect changed the history.
    /// </summary>
    None = 0,

    /// <summary>
    /// Tool-result masking reduced the history without semantic summarization.
    /// </summary>
    Masking = 1,

    /// <summary>
    /// LLM-generated summary replacement reduced the history.
    /// </summary>
    Summarization = 2,

    /// <summary>
    /// Emergency truncation reduced the history without a prior masking or summarization effect.
    /// </summary>
    EmergencyTruncation = 3,

    /// <summary>
    /// Masking ran first and emergency truncation dropped additional messages afterward.
    /// </summary>
    MaskingWithEmergencyTruncation = 4,
}
