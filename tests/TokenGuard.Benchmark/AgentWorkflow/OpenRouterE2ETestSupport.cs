using OpenAI;
using OpenAI.Chat;
using TokenGuard.Benchmark.Helpers;
using TokenGuard.Tools.Tools;

namespace TokenGuard.Benchmark.AgentWorkflow;

/// <summary>
/// Builds live OpenRouter test dependencies used by the E2E agent-loop suite.
/// </summary>
public static class OpenRouterE2ETestSupport
{
    private const string ModelName = "qwen/qwen3.6-plus";
    private static readonly Uri OpenRouterEndpoint = new("https://openrouter.ai/api/v1");

    /// <summary>
    /// Creates a chat client bound to the shared OpenRouter test model.
    /// </summary>
    /// <param name="modelName"></param>
    public static ChatClient CreateChatClient(string? modelName = ModelName)
    {
        var client = new OpenAIClient(
            new System.ClientModel.ApiKeyCredential(TestEnvironment.RequireVariable("OPENROUTER_API_KEY")),
            new OpenAIClientOptions { Endpoint = OpenRouterEndpoint });

        return client.GetChatClient(modelName);
    }

    /// <summary>
    /// Creates the constrained file-system tool set exposed to the model for a temporary workspace.
    /// </summary>
    public static ITool[] CreateTools(string workspaceDirectory) =>
    [
        new ListFilesTool(workspaceDirectory),
        new ReadFileTool(workspaceDirectory),
        new CreateTextFileTool(workspaceDirectory),
        new EditTextFileTool(workspaceDirectory),
    ];

    /// <summary>
    /// Converts TokenGuard tool definitions into OpenAI chat tool descriptors.
    /// </summary>
    public static ChatCompletionOptions CreateChatOptions(IEnumerable<ITool> tools)
    {
        var chatOptions = new ChatCompletionOptions();

        foreach (var tool in tools)
        {
            chatOptions.Tools.Add(tool.ParametersSchema is null
                ? ChatTool.CreateFunctionTool(tool.Name, tool.Description)
                : ChatTool.CreateFunctionTool(
                    functionName: tool.Name,
                    functionDescription: tool.Description,
                    functionParameters: BinaryData.FromString(tool.ParametersSchema.RootElement.GetRawText())));
        }

        return chatOptions;
    }
}
