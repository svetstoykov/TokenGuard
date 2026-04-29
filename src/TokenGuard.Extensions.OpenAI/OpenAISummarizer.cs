using OpenAI.Chat;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Models;
using TokenGuard.Core.Summarization;

namespace TokenGuard.Extensions.OpenAI;

internal sealed class OpenAISummarizer : ILlmSummarizer
{
    private readonly ChatClient _client;

    public OpenAISummarizer(ChatClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        this._client = client;
    }

    public async Task<string> SummarizeAsync(
        IReadOnlyList<ContextMessage> messages,
        int targetTokens,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        if (targetTokens <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetTokens), "targetTokens must be greater than zero.");
        }

        var completion = (await this._client.CompleteChatAsync(
                [
                    new SystemChatMessage(ConversationSummaryPrompt.SystemPrompt),
                    new UserChatMessage(ConversationSummaryPrompt.BuildUserPrompt(messages, targetTokens)),
                ],
                new ChatCompletionOptions
                {
                    MaxOutputTokenCount = targetTokens,
                },
                cancellationToken)
            .ConfigureAwait(false)).Value;

        var summary = string.Join(
                Environment.NewLine,
                completion.TextSegments()
                    .Select(segment => segment.Content)
                    .Where(static content => !string.IsNullOrWhiteSpace(content)))
            .Trim();

        return string.IsNullOrWhiteSpace(summary)
            ? throw new InvalidOperationException("OpenAI summarization returned an empty answer.")
            : summary;
    }
}
