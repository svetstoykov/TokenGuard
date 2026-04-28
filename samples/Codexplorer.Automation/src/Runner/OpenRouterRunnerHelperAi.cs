using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Codexplorer.Automation.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Codexplorer.Automation.Runner;

internal sealed class OpenRouterRunnerHelperAi : IRunnerHelperAi
{
    private const string OpenRouterApiKeyEnvironmentVariable = "OPENROUTER_API_KEY";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly AutomationHelperAiOptions _options;
    private readonly string _apiKey;
    private readonly ILogger<OpenRouterRunnerHelperAi> _logger;

    public OpenRouterRunnerHelperAi(
        HttpClient httpClient,
        IOptions<CodexplorerAutomationOptions> options,
        IConfiguration configuration,
        ILogger<OpenRouterRunnerHelperAi> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);

        this._httpClient = httpClient;
        this._options = options.Value.HelperAi;
        var configuredApiKey = configuration[OpenRouterApiKeyEnvironmentVariable]
            ?? this._options.ApiKey;
        this._apiKey = !string.IsNullOrWhiteSpace(configuredApiKey)
            ? configuredApiKey
            : throw new InvalidOperationException("Runner helper AI API key is not configured.");
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

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, this._options.Endpoint);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", this._apiKey);
        httpRequest.Headers.Accept.ParseAdd("application/json");
        httpRequest.Content = JsonContent.Create(
            new OpenRouterChatRequest(
                this._options.ModelName!,
                [
                    new OpenRouterMessage("system", HelperSystemPrompt),
                    new OpenRouterMessage("user", CreateUserPrompt(request))
                ],
                this._options.MaxOutputTokens,
                this._options.Temperature),
            options: JsonOptions);

        using var response = await this._httpClient.SendAsync(httpRequest, ct).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Runner helper AI request failed with status {(int)response.StatusCode} {response.ReasonPhrase}. Response: {responseBody}");
        }

        var completion = JsonSerializer.Deserialize<OpenRouterChatResponse>(responseBody, JsonOptions)
            ?? throw new InvalidOperationException("Runner helper AI returned an empty JSON payload.");
        var answer = completion.Choices.FirstOrDefault()?.Message.Content?.Trim();

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

    private sealed record OpenRouterChatRequest(
        string Model,
        IReadOnlyList<OpenRouterMessage> Messages,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        double Temperature);

    private sealed record OpenRouterMessage(string Role, string Content);

    private sealed record OpenRouterChatResponse(IReadOnlyList<OpenRouterChoice> Choices);

    private sealed record OpenRouterChoice(OpenRouterMessage Message);
}
