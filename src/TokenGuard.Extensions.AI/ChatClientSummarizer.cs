using Microsoft.Extensions.AI;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Summarization;

namespace TokenGuard.Extensions.AI;

/// <summary>Sends TokenGuard's summarization requests through a chat client.</summary>
/// <remarks>The caller owns the client, which is shared across contexts and must be safe for concurrent use.</remarks>
internal sealed class ChatClientSummarizer : ILlmSummarizer
{
    private readonly IChatClient _chatClient;

    /// <summary>Initializes a new instance of the <see cref="ChatClientSummarizer" /> class.</summary>
    /// <param name="chatClient">The client used for summaries. Cannot be <see langword="null" />.</param>
    /// <exception cref="ArgumentNullException"><paramref name="chatClient" /> is <see langword="null" />.</exception>
    public ChatClientSummarizer(IChatClient chatClient)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        this._chatClient = chatClient;
    }

    /// <inheritdoc />
    public async Task<SummarizerResponse> SummarizeAsync(SummarizerRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ChatMessage[] messages = [new(ChatRole.System, request.SystemPrompt), new(ChatRole.User, request.UserPrompt)];
        var options = new ChatOptions { MaxOutputTokens = request.MaxOutputTokens };
        var response = await this._chatClient.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        var text = string.Join(Environment.NewLine, response.TextSegments().Select(segment => segment.Content));
        var reason = response.FinishReason == ChatFinishReason.Stop ? SummarizerFinishReason.Completed
            : response.FinishReason == ChatFinishReason.Length ? SummarizerFinishReason.OutputLimit
            : response.FinishReason == ChatFinishReason.ContentFilter ? SummarizerFinishReason.Refused
            : SummarizerFinishReason.Other;

        return new SummarizerResponse(text, reason)
        {
            InputTokens = response.InputTokens(),
            OutputTokens = ToUsageCount(response.Usage?.OutputTokenCount),
            ReasoningTokens = ToUsageCount(response.Usage?.ReasoningTokenCount),
        };
    }

    private static int? ToUsageCount(long? tokens) => tokens is >= 0 ? (int)Math.Min(tokens.Value, int.MaxValue) : null;
}
