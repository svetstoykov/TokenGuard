using System.Diagnostics;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Diagnostics;
using TokenGuard.Core.Models;
using TokenGuard.Core.Summarization;

namespace TokenGuard.Extensions.Anthropic;

internal sealed class AnthropicSummarizer : ILlmSummarizer
{
    private const string ProviderName = "Anthropic";

    private readonly AnthropicClient _client;
    private readonly string _model;
    private readonly IConversationSummaryFormatter _formatter;
    private readonly ILogger _logger;

    public AnthropicSummarizer(AnthropicClient client, string model)
        : this(client, model, ConversationSummaryFormatter.Default)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="AnthropicSummarizer" /> class that logs each provider call.
    /// </summary>
    /// <param name="client">The Anthropic client used to generate summaries. Cannot be <see langword="null" />.</param>
    /// <param name="model">The model identifier used for summarization requests. Cannot be empty.</param>
    /// <param name="logger">The logger that receives the call records. Cannot be <see langword="null" />.</param>
    public AnthropicSummarizer(AnthropicClient client, string model, ILogger<AnthropicSummarizer> logger)
        : this(client, model, ConversationSummaryFormatter.Default, logger)
    {
    }

    internal AnthropicSummarizer(AnthropicClient client, string model, IConversationSummaryFormatter formatter)
        : this(client, model, formatter, NullLogger<AnthropicSummarizer>.Instance)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="AnthropicSummarizer" /> class with a custom prompt formatter and a logger.
    /// </summary>
    /// <param name="client">The Anthropic client used to generate summaries. Cannot be <see langword="null" />.</param>
    /// <param name="model">The model identifier used for summarization requests. Cannot be empty.</param>
    /// <param name="formatter">The formatter that builds the summarization prompt. Cannot be <see langword="null" />.</param>
    /// <param name="logger">The logger that receives the call records. Cannot be <see langword="null" />.</param>
    internal AnthropicSummarizer(
        AnthropicClient client, string model, IConversationSummaryFormatter formatter, ILogger<AnthropicSummarizer> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(formatter);
        ArgumentNullException.ThrowIfNull(logger);

        this._client = client;
        this._model = model;
        this._formatter = formatter;
        this._logger = logger;
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

        var logCall = this._logger.IsEnabled(LogLevel.Debug);
        var startTimestamp = logCall ? Stopwatch.GetTimestamp() : 0;
        SummarizerLog.SummarizerCallStarting(this._logger, ProviderName, this._model, messages.Count, targetTokens);

        try
        {
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

            if (string.IsNullOrWhiteSpace(summary))
                throw new InvalidOperationException("Anthropic summarization returned an empty answer.");

            if (logCall)
            {
                SummarizerLog.SummarizerCallCompleted(
                    this._logger, ProviderName, Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds, summary.Length,
                    response.Usage.InputTokens, response.Usage.OutputTokens);
            }

            return summary;
        }
        catch (Exception exception) when (logCall)
        {
            SummarizerLog.SummarizerCallFailed(
                this._logger, ProviderName, Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds, exception.GetType().Name);
            throw;
        }
    }
}
