namespace TokenGuard.Core.Defaults;

/// <summary>
/// Defines library defaults for LLM summarization compaction.
/// </summary>
/// <remarks>
/// The default profile protects the newest five messages verbatim and replaces any older history with a single
/// summary message. The value favors short recent continuity while still reclaiming most of the conversation body.
/// </remarks>
internal static class LlmSummarizationDefaults
{
    /// <summary>
    /// Gets the default number of newest messages that remain verbatim at the tail.
    /// </summary>
    internal const int WindowSize = 5;
}
