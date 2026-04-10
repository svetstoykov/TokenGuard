using Microsoft.Extensions.Configuration;
using OpenAI;
using OpenAI.Chat;
using TokenGuard.Core.Models;
using TokenGuard.Extensions.OpenAI;
using TokenGuard.Tools.Tools;

namespace TokenGuard.Samples.Console.AgentLoops.Providers;

internal sealed class OpenRouterAgentLoopProvider : IAgentLoopProvider
{
    private readonly ChatClient _chatClient;
    private readonly ChatCompletionOptions _chatOptions;

    public OpenRouterAgentLoopProvider(IConfiguration configuration, AgentLoopOptions options)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var apiKey = configuration["OpenRouterAPIKey"] ?? Environment.GetEnvironmentVariable("OPENROUTER_API_KEY") ?? "sk-test-key";
        var endpoint = new Uri(options.Endpoint ?? "https://openrouter.ai/api/v1");
        var client = new OpenAIClient(new System.ClientModel.ApiKeyCredential(apiKey), new OpenAIClientOptions { Endpoint = endpoint });

        this.ModelId = options.ModelId;
        this._chatClient = client.GetChatClient(this.ModelId);
        this._chatOptions = new ChatCompletionOptions();
    }

    public string Name => "OpenRouter";

    public string ModelId { get; }

    public Task<ProviderTurnResult> ExecuteTurnAsync(
        IReadOnlyList<ContextMessage> preparedMessages,
        IReadOnlyList<ITool> tools,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparedMessages);
        ArgumentNullException.ThrowIfNull(tools);

        var openAiMessages = preparedMessages.ForOpenAI();
        var options = CreateChatOptions(tools);
        return ExecuteAsync(openAiMessages, options, cancellationToken);
    }

    private async Task<ProviderTurnResult> ExecuteAsync(
        IReadOnlyList<ChatMessage> openAiMessages,
        ChatCompletionOptions options,
        CancellationToken cancellationToken)
    {
        var response = (await this._chatClient.CompleteChatAsync(openAiMessages, options, cancellationToken)).Value;

        return new ProviderTurnResult(
            response.InputTokens(),
            response.ResponseSegments(),
            response.ToolCalls
                .Select(call => new ProviderToolCall(call.Id, call.FunctionName, call.FunctionArguments.ToString()))
                .ToList());
    }

    private static ChatCompletionOptions CreateChatOptions(IReadOnlyList<ITool> tools)
    {
        var options = new ChatCompletionOptions();

        foreach (var tool in tools)
        {
            options.Tools.Add(tool.Name switch
            {
                "list_files" => ChatTool.CreateFunctionTool(
                    functionName: tool.Name,
                    functionDescription: tool.Description),
                _ => ChatTool.CreateFunctionTool(
                    functionName: tool.Name,
                    functionDescription: tool.Description,
                    functionParameters: BinaryData.FromString(tool.ParametersSchema!.RootElement.GetRawText())),
            });
        }

        return options;
    }
}
