using TokenGuard.Core.Models;

namespace TokenGuard.Core.Abstractions;

/// <summary>
/// Defines how the summarization strategy turns a slice of older history into summary text.
/// </summary>
/// <remarks>
/// <para>
/// The strategy decides which messages to summarize, the target size, and the request budget; an implementation produces
/// the text. <see cref="Summarization.SummarizerInvoker"/> is the implementation the builder wires: it builds the prompt,
/// calls an <see cref="ILlmSummarizer"/>, and checks and logs the answer.
/// </para>
/// <para>
/// The strategy calls it only with a target inside the configured <c>[MinSummaryTokens, MaxSummaryTokens]</c> range. The
/// target is an upper bound, so a smaller summary is acceptable.
/// </para>
/// </remarks>
internal interface IHistorySummarizer
{
    /// <summary>
    /// Asynchronously produces summary text for the supplied message slice.
    /// </summary>
    /// <param name="messages">The ordered source messages to summarize.</param>
    /// <param name="targetTokens">The largest summary size to ask for, in tokens. Always positive.</param>
    /// <param name="requestTokenBudget">
    /// The largest request to send, in tokens, counting its instructions and the transcript of <paramref name="messages"/>.
    /// The strategy passes the tokens available to the compacted conversation.
    /// </param>
    /// <param name="cancellationToken">A token that can cancel the summarization request.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the summary text.</returns>
    Task<string> SummarizeAsync(
        IReadOnlyList<ContextMessage> messages,
        int targetTokens,
        int requestTokenBudget,
        CancellationToken cancellationToken = default);
}
