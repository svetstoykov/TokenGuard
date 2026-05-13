namespace TokenGuard.Core.Defaults;

/// <summary>
/// Defines library defaults for LLM summarization compaction.
/// </summary>
/// <remarks>
/// The default profile protects the newest five messages verbatim and replaces any older history with a single
/// summary message. The summary budget is bounded to a 2 048–4 096 token range: requests below the minimum are
/// skipped rather than forwarded with a near-zero budget, and requests above the maximum are clamped so the
/// summarizer is never asked to fill an unbounded remainder.
/// </remarks>
internal static class LlmSummarizationDefaults
{
    /// <summary>
    /// Gets the default number of newest messages that remain verbatim at the tail.
    /// </summary>
    internal const int WindowSize = 5;

    /// <summary>
    /// Gets the default minimum token budget required before a summarization request is issued.
    /// </summary>
    internal const int MinSummaryTokens = 2048;

    /// <summary>
    /// Gets the default maximum token budget forwarded to the summarizer as a target.
    /// </summary>
    internal const int MaxSummaryTokens = 4096;
}
