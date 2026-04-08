namespace SemanticFold;

/// <summary>
/// Tracks what has happened to a message during compaction.
/// </summary>
public enum CompactionState
{
    /// <summary>
    /// The message is unmodified.
    /// </summary>
    Original,

    /// <summary>
    /// Tool result content has been replaced with a placeholder.
    /// </summary>
    Masked,

    /// <summary>
    /// The message is a synthetic summary replacing one or more original messages.
    /// </summary>
    Summarized,
}
