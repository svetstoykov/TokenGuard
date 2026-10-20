using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Models;
using TokenGuard.Core.Summarization;

namespace TokenGuard.Tests.Diagnostics;

/// <summary>
///     Represents a summarizer whose answer for each call is produced by a test-supplied delegate.
/// </summary>
/// <remarks>
///     It serves both the public model-call seam that the builder registers and the internal seam a strategy built directly
///     in a test calls. Through the public seam each answer is a completed response.
/// </remarks>
/// <param name="summarize">Produces the summary from the one-based call number, or throws to simulate a provider failure.</param>
internal sealed class ScriptedSummarizer(Func<int, string> summarize) : ILlmSummarizer, IHistorySummarizer
{
    /// <summary>
    ///     Gets the number of times the summarizer was called.
    /// </summary>
    public int Calls { get; private set; }

    /// <summary>
    ///     Creates a summarizer that always returns the same summary.
    /// </summary>
    /// <param name="summary">The summary to return.</param>
    /// <returns>The summarizer.</returns>
    public static ScriptedSummarizer Returning(string summary) => new(_ => summary);

    /// <summary>
    ///     Creates a summarizer that always throws the same exception.
    /// </summary>
    /// <param name="exception">The exception to throw.</param>
    /// <returns>The summarizer.</returns>
    public static ScriptedSummarizer Throwing(Exception exception) => new(_ => throw exception);

    /// <inheritdoc />
    public Task<SummarizerResponse> SummarizeAsync(SummarizerRequest request, CancellationToken cancellationToken = default)
    {
        this.Calls++;
        return Task.FromResult(new SummarizerResponse(summarize(this.Calls), SummarizerFinishReason.Completed));
    }

    /// <inheritdoc />
    public Task<string> SummarizeAsync(
        IReadOnlyList<ContextMessage> messages, int targetTokens, int requestTokenBudget, CancellationToken cancellationToken = default)
    {
        this.Calls++;
        return Task.FromResult(summarize(this.Calls));
    }
}
