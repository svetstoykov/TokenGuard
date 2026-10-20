using TokenGuard.Core.Models;

namespace TokenGuard.Core.Abstractions;

/// <summary>
/// Defines transcript and prompt formatting for LLM-backed conversation summarization.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IConversationSummaryFormatter"/> owns the deterministic shaping layer that sits between raw
/// <see cref="ContextMessage"/> history and the summarizer call. It decides how messages appear in the summarization
/// transcript, so every <see cref="ILlmSummarizer"/> implementation receives the same transcript for the same messages.
/// </para>
/// <para>
/// Implementations may compact verbose tool outputs aggressively as long as they preserve enough signal for the
/// downstream model to reconstruct task state. The formatting contract is intentionally non-LLM and deterministic so
/// prompt size remains predictable even when tool payloads are very large.
/// </para>
/// </remarks>
internal interface IConversationSummaryFormatter
{
    /// <summary>
    /// Builds the user prompt payload sent to the summarization model.
    /// </summary>
    /// <remarks>
    /// Implementations should embed the formatted transcript and restate the target token budget so every summarizer
    /// sends the same instructions for the same message slice.
    /// </remarks>
    /// <param name="messages">The ordered source messages that should appear in the summarization transcript.</param>
    /// <param name="targetTokens">The maximum token budget requested for the generated summary.</param>
    /// <param name="requestTokenBudget">
    /// The largest request to build, in tokens, counting <paramref name="systemPrompt"/> and the returned user prompt. A
    /// transcript above it is shortened; the newest exchange is always included, even when it alone is above the budget.
    /// </param>
    /// <param name="systemPrompt">The system prompt sent with the user prompt, counted against <paramref name="requestTokenBudget"/>.</param>
    /// <returns>The fully formatted user prompt content for one summarization request.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="messages"/> or <paramref name="systemPrompt"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="targetTokens"/> is less than or equal to zero.</exception>
    string BuildUserPrompt(IReadOnlyList<ContextMessage> messages, int targetTokens, int requestTokenBudget, string systemPrompt);

    /// <summary>
    /// Formats the supplied messages into the compact transcript consumed by the summarization prompt.
    /// </summary>
    /// <remarks>
    /// The returned transcript is optimized for prompt budget, not for lossless round-tripping. Tool result payloads
    /// may therefore appear in full, as excerpts, or as metadata-only stubs depending on implementation policy.
    /// </remarks>
    /// <param name="messages">The ordered source messages that should be rendered into transcript form.</param>
    /// <returns>The compact transcript string.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="messages"/> is <see langword="null"/>.</exception>
    string FormatTranscript(IReadOnlyList<ContextMessage> messages);
}
