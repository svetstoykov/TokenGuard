using Anthropic;
using Anthropic.Models.Messages;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Models;
using TokenGuard.Core.Summarization;

namespace TokenGuard.Extensions.Anthropic;

internal sealed class AnthropicSummarizer : ILlmSummarizer
{
    private readonly AnthropicClient _client;
    private readonly string _model;
    private readonly IConversationSummaryFormatter _formatter;

    public AnthropicSummarizer(AnthropicClient client, string model)
        : this(client, model, ConversationSummaryFormatter.Default)
    {
    }

    internal AnthropicSummarizer(AnthropicClient client, string model, IConversationSummaryFormatter formatter)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(formatter);

        this._client = client;
        this._model = model;
        this._formatter = formatter;
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

        var response = await this._client.Messages.Create(
                new MessageCreateParams
                {
                    Model = this._model,
                    MaxTokens = targetTokens,
                    System = new MessageCreateParamsSystem(
                    [
                        new TextBlockParam
                        {
                            Text = ConversationSummaryPrompt.SystemPrompt,
                        },
                    ]),
                    Messages =
                    [
                        new MessageParam
                        {
                            Role = Role.User,
                            Content = new MessageParamContent(
                            [
                                new TextBlockParam
                                {
                                    Text = this._formatter.BuildUserPrompt(messages, targetTokens),
                                },
                            ]),
                        },
                    ],
                },
                cancellationToken)
            .ConfigureAwait(false);

        var summary = string.Join(
                Environment.NewLine,
                response.TextSegments()
                    .Select(segment => segment.Content)
                    .Where(static content => !string.IsNullOrWhiteSpace(content)))
            .Trim();

        return string.IsNullOrWhiteSpace(summary)
            ? throw new InvalidOperationException("Anthropic summarization returned an empty answer.")
            : summary;
    }
}
