namespace TokenGuard.Core.Enums;

/// <summary>
/// Identifies which threshold caused a compaction cycle to begin.
/// </summary>
public enum CompactionTrigger
{
    /// <summary>
    /// Compaction was triggered by the normal compaction threshold.
    /// </summary>
    Normal,

    /// <summary>
    /// Compaction was triggered by the emergency compaction threshold.
    /// </summary>
    Emergency,
}
