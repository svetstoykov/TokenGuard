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
/// The caller computes <c>targetTokens</c> from the remaining budget after protected history is preserved. That value
/// can be zero or negative when the protected tail already consumes the entire budget, so implementations must treat
/// it as guidance rather than as a guarantee that summary output will fit.
/// </para>
/// </remarks>
public interface ILlmSummarizer
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
    /// <param name="targetTokens">The approximate token budget available for the summary text.</param>
    /// <param name="cancellationToken">A token that can cancel the summarization request.</param>
    /// <returns>A task that resolves to the summary text generated for <paramref name="messages"/>.</returns>
    Task<string> SummarizeAsync(
        IReadOnlyList<ContextMessage> messages,
        int targetTokens,
        CancellationToken cancellationToken = default);
}
