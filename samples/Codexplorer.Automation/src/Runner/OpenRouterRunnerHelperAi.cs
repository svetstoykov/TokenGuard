using Codexplorer.Automation.Configuration;
using Codexplorer.Automation.Scoring;
using Codexplorer.Measurements;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;

namespace Codexplorer.Automation.Runner;

/// <summary>Receives typed helper responses from OpenRouter.</summary>
internal sealed class OpenRouterRunnerHelperAi : IRunnerHelperAi
{
    private readonly ChatClient _chatClient;
    private readonly AutomationHelperAiOptions _options;
    private readonly ILogger<OpenRouterRunnerHelperAi> _logger;

    /// <summary>Initializes a new instance of the <see cref="OpenRouterRunnerHelperAi" /> class.</summary>
    /// <param name="options">The runner configuration.</param>
    /// <param name="configuration">The effective configuration containing environment credentials.</param>
    /// <param name="logger">The helper request logger.</param>
    public OpenRouterRunnerHelperAi(
        IOptions<CodexplorerAutomationOptions> options,
        IConfiguration configuration,
        ILogger<OpenRouterRunnerHelperAi> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);

        this._options = options.Value.HelperAi;

        var apiKey = HelperAiCredentials.Resolve(configuration, this._options);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Runner helper AI API key is not configured.");
        }
        var modelName = this._options.ModelName ?? throw new InvalidOperationException("Runner helper AI model name is not configured.");

        var endpoint = this._options.Endpoint ?? throw new InvalidOperationException("Runner helper AI endpoint is not configured.");
        this._chatClient = new OpenAIClient(new System.ClientModel.ApiKeyCredential(apiKey),
            new OpenAIClientOptions { Endpoint = new Uri(endpoint) }).GetChatClient(modelName);

        this._logger = logger;
    }

    /// <inheritdoc />
    public async Task<RunnerHelperAiResult> AnswerAsync(RunnerHelperAiRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        this._logger.LogInformation(
            "Requesting helper AI answer for task {TaskId} with model {ModelName}. TurnsConsumed={TurnsConsumed}/{MaxTurns}.",
            request.TaskId,
            this._options.ModelName,
            request.TurnsConsumed,
            request.MaxTurns);

        var completion = (await this._chatClient.CompleteChatAsync(
                CreateMessages(request),
                new ChatCompletionOptions
                {
                    MaxOutputTokenCount = this._options.MaxOutputTokens,
                    Temperature = (float)this._options.Temperature
                },
                ct)
            .ConfigureAwait(false)).Value;
        var answer = string.Join(
                Environment.NewLine,
                completion.Content
                    .Select(part => part.Text)
                    .Where(static text => !string.IsNullOrWhiteSpace(text)))
            .Trim();

        this._logger.LogInformation(
            "Generated helper answer for task {TaskId} after runner question. AnswerLength={AnswerLength}.",
            request.TaskId,
            answer.Length);

        return new RunnerHelperAiResult(string.IsNullOrWhiteSpace(answer) ? null : answer, new UsageMeasurement
        {
            InputTokens = completion.Usage?.InputTokenCount,
            OutputTokens = completion.Usage?.OutputTokenCount
        });
    }

    /// <summary>Composes the exact outgoing messages and removes a declared code from both complete prompts.</summary>
    /// <param name="request">The original helper request with an optional probe code.</param>
    /// <returns>The typed model messages.</returns>
    internal static IReadOnlyList<ChatMessage> CreateMessages(RunnerHelperAiRequest request)
    {
        var system = HelperSystemPrompt;
        var user = CreateUserPrompt(request);
        if (request.ProbeCanary is { } canary)
        {
            var normalized = TextNormalizer.Normalize(canary);
            system = TextNormalizer.Normalize(system).Replace(normalized, "[...]", StringComparison.OrdinalIgnoreCase);
            user = TextNormalizer.Normalize(user).Replace(normalized, "[...]", StringComparison.OrdinalIgnoreCase);
        }
        return [new SystemChatMessage(system), new UserChatMessage(user)];
    }

    private static string CreateUserPrompt(RunnerHelperAiRequest request)
    {
        return
            $"""
            Task id: {request.TaskId}
            Task size: {request.TaskSize}
            Workspace path: {request.WorkspacePath}
            Initial task prompt:
            {request.InitialPrompt}

            Latest assistant text:
            {request.AssistantText ?? "(none)"}

            Runner question:
            {request.RunnerQuestion}

            Turns consumed so far: {request.TurnsConsumed}
            Planned task turn budget: {request.MaxTurns}
            Planned wrap-up window: {request.WrapUpWindow}
            Wrap-up already sent: {request.WrapUpSent}
            The model-call budget is a hard limit.

            Return one direct answer message that runner can submit back into same Codexplorer session.
            """;
    }

    private const string HelperSystemPrompt =
        """
        You are helper AI for Codexplorer automation runner.
        Answer only explicit clarification questions from Codexplorer.
        You are not main orchestrator.
        Reply with exactly one concise user message runner can send back into same session.
        Do not ask follow-up questions.
        Prefer concrete decisions over meta-discussion.
        If context is missing, state the missing fact briefly and give the safest useful direction you can.
        You should at all times give an answer that allows the flow to continue forward, to the best of your effort.
        Never in doubt and never ask, questions. Simply anwser, always.
        """;
}
