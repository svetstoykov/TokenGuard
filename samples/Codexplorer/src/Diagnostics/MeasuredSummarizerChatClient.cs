using System.ClientModel;
using OpenAI.Chat;

namespace Codexplorer.Diagnostics;

/// <summary>Captures provider usage before the summarizer validates its answer.</summary>
internal sealed class MeasuredSummarizerChatClient : ChatClient
{
    private readonly ChatClient _client;
    private readonly ISessionMeasurementCollector _collector;

    /// <summary>Initializes a new instance of the <see cref="MeasuredSummarizerChatClient" /> class.</summary>
    /// <param name="client">The actual provider client.</param>
    /// <param name="collector">The singleton session collector.</param>
    public MeasuredSummarizerChatClient(ChatClient client, ISessionMeasurementCollector collector)
    {
        this._client = client;
        this._collector = collector;
    }

    /// <inheritdoc />
    public override async Task<ClientResult<ChatCompletion>> CompleteChatAsync(
        IEnumerable<ChatMessage> messages, ChatCompletionOptions? options = null, CancellationToken cancellationToken = default)
    {
        var result = await this._client.CompleteChatAsync(messages, options, cancellationToken).ConfigureAwait(false);
        this._collector.SummarizerResponded(result.Value.Usage?.InputTokenCount, result.Value.Usage?.OutputTokenCount);
        return result;
    }
}
