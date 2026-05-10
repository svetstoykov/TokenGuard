using OpenAI;
using OpenAI.Chat;

namespace Codexplorer.Configuration;

internal static class OpenRouterChatClientFactory
{
    private static readonly Uri OpenRouterEndpoint = new("https://openrouter.ai/api/v1");

    internal static ChatClient Create(CodexplorerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var openRouterOptions = options.OpenRouter
            ?? throw new InvalidOperationException("Codexplorer OpenRouter options are not configured.");
        var modelOptions = options.Model
            ?? throw new InvalidOperationException("Codexplorer model options are not configured.");
        var apiKey = openRouterOptions.ApiKey;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Codexplorer OpenRouter API key is not configured.");
        }

        var modelName = modelOptions.Name
            ?? throw new InvalidOperationException("Codexplorer model name is not configured.");
        var client = new OpenAIClient(
            new System.ClientModel.ApiKeyCredential(apiKey),
            new OpenAIClientOptions { Endpoint = OpenRouterEndpoint });

        return client.GetChatClient(modelName);
    }
}
