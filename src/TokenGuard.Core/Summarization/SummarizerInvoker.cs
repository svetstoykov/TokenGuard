using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Defaults;
using TokenGuard.Core.Diagnostics;
using TokenGuard.Core.Models;

namespace TokenGuard.Core.Summarization;

/// <summary>
/// Turns a slice of history into summary text through one <see cref="ILlmSummarizer"/> call, and checks and logs the answer.
/// </summary>
/// <remarks>
/// <para>
/// The invoker builds the request: the system prompt from the configured instructions and the fixed lines, the user
/// prompt with the target size and the transcript built to the request budget, and an output cap of twice the target.
/// </para>
/// <para>
/// A response that is <see langword="null"/>, <see cref="SummarizerFinishReason.Refused"/>, or blank throws
/// <see cref="InvalidOperationException"/>. A <see cref="SummarizerFinishReason.OutputLimit"/> response is accepted and logs
/// a warning that the summary was cut. Each call writes the summarizer log events under the logger it was given and adds
/// the response values to the current summarize activity.
/// </para>
/// <para>
/// The invoker holds no per-call state, so one instance can serve concurrent calls when its summarizer can.
/// </para>
/// </remarks>
internal sealed class SummarizerInvoker : IHistorySummarizer
{
    private readonly ILlmSummarizer _summarizer;
    private readonly IConversationSummaryFormatter _formatter;
    private readonly string _systemPrompt;
    private readonly string _summarizerName;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SummarizerInvoker"/> class.
    /// </summary>
    /// <param name="summarizer">The model call that writes the summary. Cannot be <see langword="null"/>.</param>
    /// <param name="summaryInstructions">
    /// The summary instructions, or <see langword="null"/> for <see cref="ConversationSummaryPrompt.DefaultInstructions"/>.
    /// </param>
    /// <param name="logger">The logger that receives the summarizer call records. Cannot be <see langword="null"/>.</param>
    /// <param name="tokenCounter">The context counter for request budgeting, or <see langword="null"/> for the default estimator.</param>
    /// <exception cref="ArgumentNullException"><paramref name="summarizer"/> or <paramref name="logger"/> is <see langword="null"/>.</exception>
    public SummarizerInvoker(ILlmSummarizer summarizer, string? summaryInstructions, ILogger logger, ITokenCounter? tokenCounter = null)
    {
        ArgumentNullException.ThrowIfNull(summarizer);
        ArgumentNullException.ThrowIfNull(logger);

        this._summarizer = summarizer;

        this._formatter = tokenCounter is null ? ConversationSummaryFormatter.Default : new ConversationSummaryFormatter(tokenCounter);
        this._systemPrompt = ConversationSummaryPrompt.BuildSystemPrompt(summaryInstructions);
        this._summarizerName = summarizer.GetType().Name;
        this._logger = logger;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// The summarizer returned no response, a refusal, or a blank answer.
    /// </exception>
    public async Task<string> SummarizeAsync(
        IReadOnlyList<ContextMessage> messages,
        int targetTokens,
        int requestTokenBudget,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetTokens);

        var logCall = this._logger.IsEnabled(LogLevel.Debug);
        var startTimestamp = logCall ? Stopwatch.GetTimestamp() : 0;
        SummarizerLog.SummarizerCallStarting(this._logger, this._summarizerName, model: null, messages.Count, targetTokens);

        var outputCap = (int)Math.Min((long)targetTokens * LlmSummarizationDefaults.OutputCapFactor, int.MaxValue);
        SummarizerResponse? response = null;

        try
        {
            var userPrompt = this._formatter.BuildUserPrompt(messages, targetTokens, requestTokenBudget, this._systemPrompt);
            var request = new SummarizerRequest(this._systemPrompt, userPrompt, outputCap);

            response = await this._summarizer.SummarizeAsync(request, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"The summarizer {this._summarizerName} returned no response.");

            TokenGuardTelemetry.RecordSummarizerResponse(FinishReasonName(response.FinishReason), response.OutputTokens, response.ReasoningTokens);

            var summary = this.AcceptedSummary(response);
            if (response.FinishReason == SummarizerFinishReason.OutputLimit)
                SummarizerLog.SummarizerOutputCut(this._logger, this._summarizerName, outputCap, response.OutputTokens);

            if (logCall)
            {
                SummarizerLog.SummarizerCallCompleted(
                    this._logger, this._summarizerName, Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds, summary.Length,
                    response.InputTokens, response.OutputTokens, response.ReasoningTokens, FinishReasonName(response.FinishReason));
            }

            return summary;
        }
        catch (Exception exception) when (logCall)
        {
            SummarizerLog.SummarizerCallFailed(
                this._logger, this._summarizerName, Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds, exception.GetType().Name,
                response?.OutputTokens, response?.ReasoningTokens, response is null ? null : FinishReasonName(response.FinishReason));
            throw;
        }
    }

    /// <summary>
    /// Returns the trimmed summary of a response, or throws when the response is a refusal or blank.
    /// </summary>
    /// <param name="response">The response the summarizer returned.</param>
    /// <returns>The summary text without leading or trailing white space.</returns>
    /// <exception cref="InvalidOperationException">The response is a refusal or its text is blank.</exception>
    private string AcceptedSummary(SummarizerResponse response)
    {
        if (response.FinishReason == SummarizerFinishReason.Refused)
        {
            var refusal = string.IsNullOrWhiteSpace(response.Text) ? "not given" : response.Text.Trim();
            throw new InvalidOperationException(
                $"The summarizer {this._summarizerName} refused to write the summary. Output tokens: {Reported(response.OutputTokens)}; "
                + $"reasoning tokens: {Reported(response.ReasoningTokens)}. Refusal: {refusal}");
        }

        if (string.IsNullOrWhiteSpace(response.Text))
        {
            throw new InvalidOperationException(
                $"The summarizer {this._summarizerName} returned an empty answer. Finish reason: {FinishReasonName(response.FinishReason)}; "
                + $"output tokens: {Reported(response.OutputTokens)}; reasoning tokens: {Reported(response.ReasoningTokens)}.");
        }

        return response.Text.Trim();
    }

    /// <summary>
    /// Returns the name that logs and traces carry for a finish reason.
    /// </summary>
    /// <param name="finishReason">The finish reason the summarizer reported.</param>
    /// <returns><c>completed</c>, <c>output_limit</c>, <c>refused</c>, or <c>other</c> for any other value.</returns>
    private static string FinishReasonName(SummarizerFinishReason finishReason) =>
        finishReason switch
        {
            SummarizerFinishReason.Completed => "completed",
            SummarizerFinishReason.OutputLimit => "output_limit",
            SummarizerFinishReason.Refused => "refused",
            _ => "other",
        };

    private static string Reported(int? tokens) => tokens?.ToString(CultureInfo.InvariantCulture) ?? "not reported";
}
