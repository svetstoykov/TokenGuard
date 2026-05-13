namespace TokenGuard.Core.Defaults;

/// <summary>
/// Defines library defaults for LLM summarization transcript formatting.
/// </summary>
/// <remarks>
/// Small tool results are preserved in full because they usually contain the entire signal the summary model needs.
/// Medium results are reduced to deterministic excerpts, and very large results collapse to metadata-only stubs so
/// transcript size stays bounded without losing evidence that the tool call happened.
/// </remarks>
internal static class ConversationSummaryFormattingDefaults
{
    /// <summary>
    /// Gets the maximum estimated token size for a tool result to be preserved in full.
    /// </summary>
    internal const int FullToolResultMaxTokens = 300;

    /// <summary>
    /// Gets the maximum estimated token size for a tool result to be rendered as an excerpt.
    /// </summary>
    internal const int ExcerptToolResultMaxTokens = 1200;

    /// <summary>
    /// Gets the number of leading lines included in excerpted tool results.
    /// </summary>
    internal const int ExcerptHeadLineCount = 12;

    /// <summary>
    /// Gets the number of trailing lines included in excerpted tool results.
    /// </summary>
    internal const int ExcerptTailLineCount = 12;

    /// <summary>
    /// Gets the maximum number of salient middle lines included in excerpted tool results.
    /// </summary>
    internal const int ExcerptSalientLineCount = 8;
}
