using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Summarization;

namespace TokenGuard.Tests.Summarization;

/// <summary>
///     Represents a model call that records each request and answers with a test-supplied response.
/// </summary>
/// <param name="respond">Produces the response for a request, or throws to simulate a failed call.</param>
internal sealed class RespondingSummarizer(Func<SummarizerRequest, SummarizerResponse?> respond) : ILlmSummarizer
{
    private readonly List<SummarizerRequest> _requests = [];

    /// <summary>
    ///     Gets the requests received so far, oldest first.
    /// </summary>
    public IReadOnlyList<SummarizerRequest> Requests
    {
        get
        {
            lock (this._requests)
                return this._requests.ToArray();
        }
    }

    /// <summary>
    ///     Creates a summarizer that always answers with the same text and finish reason.
    /// </summary>
    /// <param name="text">The text of every response.</param>
    /// <param name="finishReason">The finish reason of every response.</param>
    /// <returns>The summarizer.</returns>
    public static RespondingSummarizer Answering(string text, SummarizerFinishReason finishReason = SummarizerFinishReason.Completed) =>
        new(_ => new SummarizerResponse(text, finishReason));

    /// <inheritdoc />
    public Task<SummarizerResponse> SummarizeAsync(SummarizerRequest request, CancellationToken cancellationToken = default)
    {
        lock (this._requests)
            this._requests.Add(request);

        return Task.FromResult(respond(request)!);
    }
}
