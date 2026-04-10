using TokenGuard.Core.Models;

namespace TokenGuard.Core.Enums;

/// <summary>
/// Describes how a <see cref="SemanticMessage"/> has been transformed by compaction.
/// </summary>
/// <remarks>
/// TokenGuard preserves compaction provenance on each message so callers can inspect whether a prepared payload still
/// contains original content or a reduced representation. This is especially useful for debugging, auditing, and tests
/// that need to assert how a strategy handled older history.
/// </remarks>
public enum CompactionState
{
    /// <summary>
    /// The message is retained without compaction changes.
    /// </summary>
    Original,

    /// <summary>
    /// The message keeps its position, but one or more content segments are replaced with placeholders.
    /// </summary>
    Masked,

    /// <summary>
    /// The message is a synthetic summary standing in for one or more original messages.
    /// </summary>
    Summarized,
}
