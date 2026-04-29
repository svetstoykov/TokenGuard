using TokenGuard.Core.Models;

namespace TokenGuard.Core.Abstractions;

/// <summary>
/// Defines LLM-backed summarization for older conversation history.
/// </summary>
/// <remarks>
/// <para>
/// Implement <see cref="ILlmSummarizer"/> when TokenGuard should delegate semantic compression to an external model
/// instead of masking or dropping history. The abstraction stays provider-agnostic by accepting TokenGuard's
/// canonical <see cref="ContextMessage"/> model and returning plain summary text that strategies can re-inject into
/// the conversation.
/// </para>
/// <para>
/// The calling strategy enforces system-wide budget policy before invoking the summarizer: it skips the call
/// entirely when the remaining budget falls below the configured minimum, and clamps the target to the configured
/// maximum otherwise. Implementations can therefore assume that <c>targetTokens</c> is always a positive, bounded
/// value that represents a meaningful upper limit for a viable summary, not a directive to consume all leftover
/// budget. Producing a smaller summary is acceptable and expected.
/// </para>
/// </remarks>
internal interface ILlmSummarizer
{
    /// <summary>
    /// Produces summary text for the supplied message slice.
    /// </summary>
    /// <remarks>
    /// Implementations should preserve enough semantic state for the agent to continue after older messages are
    /// replaced by a single summary message. The returned text becomes conversation content, so callers expect a
    /// stable, reconstruction-grade summary rather than an opaque identifier or transport envelope.
    /// </remarks>
    /// <param name="messages">The ordered source messages to summarize.</param>
    /// <param name="targetTokens">
    /// A strategy-enforced upper bound on the summary token budget. Always positive. Treat as guidance for how
    /// large the summary should be, not as a requirement to fill the entire allowance.
    /// </param>
    /// <param name="cancellationToken">A token that can cancel the summarization request.</param>
    /// <returns>A task that resolves to the summary text generated for <paramref name="messages"/>.</returns>
    Task<string> SummarizeAsync(
        IReadOnlyList<ContextMessage> messages,
        int targetTokens,
        CancellationToken cancellationToken = default);
}
