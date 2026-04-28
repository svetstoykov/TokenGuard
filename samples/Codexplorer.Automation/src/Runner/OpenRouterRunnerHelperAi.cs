using Codexplorer.Automation.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;

namespace Codexplorer.Automation.Runner;

internal sealed class OpenRouterRunnerHelperAi : IRunnerHelperAi
{
    private readonly ChatClient _chatClient;
    private readonly AutomationHelperAiOptions _options;
    private readonly ILogger<OpenRouterRunnerHelperAi> _logger;

    public OpenRouterRunnerHelperAi(
        IOptions<CodexplorerAutomationOptions> options,
        IConfiguration configuration,
        ILogger<OpenRouterRunnerHelperAi> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);

        this._options = options.Value.HelperAi;
        
        var apiKey = this._options.ApiKey ?? throw new InvalidOperationException("Runner helper AI API key is not configured.");
        var modelName = this._options.ModelName ?? throw new InvalidOperationException("Runner helper AI model name is not configured.");
        
        this._chatClient = new OpenAIClient(
                new System.ClientModel.ApiKeyCredential(apiKey),
                new OpenAIClientOptions { Endpoint = new Uri(this._options.Endpoint ?? throw new InvalidOperationException("Runner helper AI endpoint is not configured.")) })
            .GetChatClient(modelName);
        
        this._logger = logger;
    }

    public async Task<string> AnswerAsync(RunnerHelperAiRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        this._logger.LogInformation(
            "Requesting helper AI answer for task {TaskId} with model {ModelName}. TurnsConsumed={TurnsConsumed}/{MaxTurns}.",
            request.TaskId,
            this._options.ModelName,
            request.TurnsConsumed,
            request.MaxTurns);

        var completion = (await this._chatClient.CompleteChatAsync(
                [
                    new SystemChatMessage(HelperSystemPrompt),
                    new UserChatMessage(CreateUserPrompt(request))
                ],
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

        if (string.IsNullOrWhiteSpace(answer))
        {
            throw new InvalidOperationException("Runner helper AI returned an empty answer.");
        }

        this._logger.LogInformation(
            "Generated helper answer for task {TaskId} after runner question. AnswerLength={AnswerLength}.",
            request.TaskId,
            answer.Length);

        return answer;
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
            Total task turn budget: {request.MaxTurns}
            Reserved wrap-up window: {request.WrapUpWindow}
            Wrap-up already sent: {request.WrapUpSent}

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
