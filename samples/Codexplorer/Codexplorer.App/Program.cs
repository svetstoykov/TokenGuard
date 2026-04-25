using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenAI;
using OpenAI.Chat;
using Serilog;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;
using TokenGuard.Extensions.OpenAI;

namespace Codexplorer.App;

internal sealed class Program
{
    private static readonly Uri OpenRouterEndpoint = new("https://openrouter.ai/api/v1");

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var builder = Host.CreateApplicationBuilder(args);

            builder.Services.AddSerilog((_, loggerConfiguration) => loggerConfiguration
                .MinimumLevel.Information()
                .WriteTo.Console()
                .WriteTo.File(
                    Path.Combine(AppContext.BaseDirectory, "logs", "codexplorer-.log"),
                    rollingInterval: RollingInterval.Day));

            builder.Services.AddSingleton<OpenRouterHelloWorldRunner>();

            using var host = builder.Build();
            return await host.Services
                .GetRequiredService<OpenRouterHelloWorldRunner>()
                .RunAsync();
        }
        catch (MissingEnvironmentVariableException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private sealed class OpenRouterHelloWorldRunner(IConfiguration configuration)
    {
        public async Task<int> RunAsync(CancellationToken cancellationToken = default)
        {
            var apiKey = GetRequiredOpenRouterApiKey();
            var modelName = GetRequiredModelName();
            var client = new OpenAIClient(
                new System.ClientModel.ApiKeyCredential(apiKey),
                new OpenAIClientOptions { Endpoint = OpenRouterEndpoint });

            IReadOnlyList<ContextMessage> prompt =
            [
                ContextMessage.FromText(
                    MessageRole.User,
                    "Say hello in one short sentence.")
            ];

            ChatCompletion completion = (await client
                .GetChatClient(modelName)
                .CompleteChatAsync(prompt.ForOpenAI(), new ChatCompletionOptions(), cancellationToken))
                .Value;

            var reply = string.Join(
                Environment.NewLine,
                completion.ResponseSegments()
                    .OfType<TextContent>()
                    .Select(segment => segment.Content)
                    .Where(static content => !string.IsNullOrWhiteSpace(content)));

            if (string.IsNullOrWhiteSpace(reply))
            {
                throw new InvalidOperationException("OpenRouter returned an empty response.");
            }

            Console.WriteLine(reply);
            return 0;
        }

        private string GetRequiredOpenRouterApiKey()
        {
            var apiKey = configuration["OPENROUTER_API_KEY"]
                ?? Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new MissingEnvironmentVariableException("OPENROUTER_API_KEY");
            }

            return apiKey;
        }

        private string GetRequiredModelName()
        {
            var modelName = configuration["Codexplorer:Model:Name"];

            if (string.IsNullOrWhiteSpace(modelName))
            {
                throw new InvalidOperationException("Missing required configuration value 'Codexplorer:Model:Name'.");
            }

            return modelName;
        }
    }

    private sealed class MissingEnvironmentVariableException(string variableName)
        : InvalidOperationException($"Missing required environment variable '{variableName}'.");
}
