using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Summarization;

namespace TokenGuard.Extensions.Anthropic;

/// <summary>
///     Sends TokenGuard's summarization requests to an Anthropic model.
/// </summary>
/// <remarks>
///     The request goes out with the system prompt as the system block, one user message, and the output cap of the
///     request as <c>max_tokens</c>. Stop reason <c>end_turn</c> or <c>stop_sequence</c> maps to
///     <see cref="SummarizerFinishReason.Completed" />, <c>max_tokens</c> to <see cref="SummarizerFinishReason.OutputLimit" />,
///     <c>refusal</c> to <see cref="SummarizerFinishReason.Refused" />, and any other or none to
///     <see cref="SummarizerFinishReason.Other" />. <see cref="AnthropicClient" /> is safe for concurrent use, so one instance
///     serves every context of a configuration.
/// </remarks>
internal sealed class AnthropicSummarizer : ILlmSummarizer
{
    private readonly AnthropicClient _client;
    private readonly string _model;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AnthropicSummarizer" /> class.
    /// </summary>
    /// <param name="client">The Anthropic client used to generate summaries. Cannot be <see langword="null" />.</param>
    /// <param name="model">The model identifier used for summarization requests. Cannot be blank.</param>
    /// <exception cref="ArgumentNullException"><paramref name="client" /> or <paramref name="model" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException"><paramref name="model" /> is empty or whitespace.</exception>
    public AnthropicSummarizer(AnthropicClient client, string model)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        this._client = client;
        this._model = model;
    }

    /// <inheritdoc />
    public async Task<SummarizerResponse> SummarizeAsync(SummarizerRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var response = await this._client.Messages.Create(
                new MessageCreateParams
                {
                    Model = this._model,
                    MaxTokens = request.MaxOutputTokens,
                    System = new MessageCreateParamsSystem([new TextBlockParam { Text = request.SystemPrompt }]),
                    Messages =
                    [
                        new MessageParam
                        {
                            Role = Role.User,
                            Content = new MessageParamContent([new TextBlockParam { Text = request.UserPrompt }]),
                        },
                    ],
                },
                cancellationToken)
            .ConfigureAwait(false);

        // The SDK throws on reading Usage when the payload carries none, so presence is checked on the raw payload.
        var usage = response.RawData.TryGetValue("usage", out var rawUsage) && rawUsage.ValueKind == JsonValueKind.Object
            ? response.Usage
            : null;

        var text = string.Join(
            Environment.NewLine,
            response.TextSegments().Select(static segment => segment.Content).Where(static content => !string.IsNullOrWhiteSpace(content)));

        return new SummarizerResponse(text, MapStopReason(response.StopReason?.Raw()))
        {
            InputTokens = ToTokenCount(usage?.InputTokens),
            OutputTokens = ToTokenCount(usage?.OutputTokens),
        };
    }

    private static SummarizerFinishReason MapStopReason(string? stopReason) =>
        stopReason switch
        {
            "end_turn" or "stop_sequence" => SummarizerFinishReason.Completed,
            "max_tokens" => SummarizerFinishReason.OutputLimit,
            "refusal" => SummarizerFinishReason.Refused,
            _ => SummarizerFinishReason.Other,
        };

    private static int? ToTokenCount(long? tokens) => tokens is null ? null : (int)Math.Min(tokens.Value, int.MaxValue);
}
