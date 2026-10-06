using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenAI.Chat;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Diagnostics;
using TokenGuard.Core.Models;
using TokenGuard.Core.Summarization;

namespace TokenGuard.Extensions.OpenAI;

internal sealed class OpenAISummarizer : ILlmSummarizer
{
    private const string ProviderName = "OpenAI";

    private readonly ChatClient _client;
    private readonly IConversationSummaryFormatter _formatter;
    private readonly ILogger _logger;

    public OpenAISummarizer(ChatClient client)
        : this(client, ConversationSummaryFormatter.Default)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="OpenAISummarizer" /> class that logs each provider call.
    /// </summary>
    /// <param name="client">The OpenAI client used to generate summaries. Cannot be <see langword="null" />.</param>
    /// <param name="logger">The logger that receives the call records. Cannot be <see langword="null" />.</param>
    public OpenAISummarizer(ChatClient client, ILogger<OpenAISummarizer> logger)
        : this(client, ConversationSummaryFormatter.Default, logger)
    {
    }

    internal OpenAISummarizer(ChatClient client, IConversationSummaryFormatter formatter)
        : this(client, formatter, NullLogger<OpenAISummarizer>.Instance)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="OpenAISummarizer" /> class with a custom prompt formatter and a logger.
    /// </summary>
    /// <param name="client">The OpenAI client used to generate summaries. Cannot be <see langword="null" />.</param>
    /// <param name="formatter">The formatter that builds the summarization prompt. Cannot be <see langword="null" />.</param>
    /// <param name="logger">The logger that receives the call records. Cannot be <see langword="null" />.</param>
    internal OpenAISummarizer(ChatClient client, IConversationSummaryFormatter formatter, ILogger<OpenAISummarizer> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(formatter);
        ArgumentNullException.ThrowIfNull(logger);

        this._client = client;
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
        SummarizerLog.SummarizerCallStarting(this._logger, ProviderName, model: null, messages.Count, targetTokens);

        try
        {
            var completion = (await this._client.CompleteChatAsync(
                    [
                        new SystemChatMessage(ConversationSummaryPrompt.SystemPrompt),
                        new UserChatMessage(this._formatter.BuildUserPrompt(messages, targetTokens)),
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

            if (string.IsNullOrWhiteSpace(summary))
                throw new InvalidOperationException("OpenAI summarization returned an empty answer.");

            if (logCall)
            {
                SummarizerLog.SummarizerCallCompleted(
                    this._logger, ProviderName, Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds, summary.Length,
                    completion.Usage?.InputTokenCount, completion.Usage?.OutputTokenCount);
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
