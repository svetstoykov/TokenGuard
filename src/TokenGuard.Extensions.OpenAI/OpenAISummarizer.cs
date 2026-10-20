using OpenAI.Chat;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Models.Content;
using TokenGuard.Core.Summarization;

namespace TokenGuard.Extensions.OpenAI;

/// <summary>
///     Sends TokenGuard's summarization requests to an OpenAI chat model.
/// </summary>
/// <remarks>
///     The request goes out as one system message and one user message with the output cap of the request. A refusal is
///     returned as <see cref="SummarizerFinishReason.Refused" /> with its text; finish reason <c>stop</c> maps to
///     <see cref="SummarizerFinishReason.Completed" />, <c>length</c> to <see cref="SummarizerFinishReason.OutputLimit" />,
///     and any other to <see cref="SummarizerFinishReason.Other" />. <see cref="ChatClient" /> is safe for concurrent use, so
///     one instance serves every context of a configuration.
/// </remarks>
internal sealed class OpenAISummarizer : ILlmSummarizer
{
    private readonly ChatClient _client;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OpenAISummarizer" /> class.
    /// </summary>
    /// <param name="client">The OpenAI client used to generate summaries. Cannot be <see langword="null" />.</param>
    /// <exception cref="ArgumentNullException"><paramref name="client" /> is <see langword="null" />.</exception>
    public OpenAISummarizer(ChatClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        this._client = client;
    }

    /// <inheritdoc />
    public async Task<SummarizerResponse> SummarizeAsync(SummarizerRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var completion = (await this._client.CompleteChatAsync(
                [new SystemChatMessage(request.SystemPrompt), new UserChatMessage(request.UserPrompt)],
                new ChatCompletionOptions { MaxOutputTokenCount = request.MaxOutputTokens },
                cancellationToken)
            .ConfigureAwait(false)).Value;

        var refused = !string.IsNullOrWhiteSpace(completion.Refusal);
        var text = refused ? completion.Refusal : JoinText(completion.TextSegments());

        return new SummarizerResponse(text, refused ? SummarizerFinishReason.Refused : MapFinishReason(completion.FinishReason))
        {
            InputTokens = completion.Usage?.InputTokenCount,
            OutputTokens = completion.Usage?.OutputTokenCount,
            ReasoningTokens = completion.Usage?.OutputTokenDetails?.ReasoningTokenCount,
        };
    }

    private static string JoinText(IEnumerable<TextContent> segments) =>
        string.Join(
            Environment.NewLine, segments.Select(static segment => segment.Content).Where(static content => !string.IsNullOrWhiteSpace(content)));

    private static SummarizerFinishReason MapFinishReason(ChatFinishReason finishReason) =>
        finishReason switch
        {
            ChatFinishReason.Stop => SummarizerFinishReason.Completed,
            ChatFinishReason.Length => SummarizerFinishReason.OutputLimit,
            _ => SummarizerFinishReason.Other,
        };
}
