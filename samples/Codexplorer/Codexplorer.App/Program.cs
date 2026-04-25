using Codexplorer.App.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
            builder.Configuration.AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false);

            builder.Services.AddCodexplorerOptions(builder.Configuration);
            builder.Services.AddSerilog((_, loggerConfiguration) => loggerConfiguration
                .MinimumLevel.Information()
                .WriteTo.Console()
                .WriteTo.File(
                    Path.Combine(AppContext.BaseDirectory, "logs", "codexplorer-.log"),
                    rollingInterval: RollingInterval.Day));

            builder.Services.AddSingleton<OpenRouterHelloWorldRunner>();

            using var host = builder.Build();
            await host.StartAsync();

            try
            {
                LogResolvedConfiguration(host.Services);

                return await host.Services
                    .GetRequiredService<OpenRouterHelloWorldRunner>()
                    .RunAsync();
            }
            finally
            {
                await host.StopAsync();
            }
        }
        catch (OptionsValidationException ex)
        {
            foreach (var failure in ex.Failures)
            {
                Console.Error.WriteLine(failure);
            }

            return 1;
        }
    }

    private static void LogResolvedConfiguration(IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        var options = services.GetRequiredService<IOptions<CodexplorerOptions>>().Value;

        logger.LogDebug("Resolved Codexplorer configuration {@CodexplorerOptions}", CreateRedactedConfigurationSnapshot(options));
    }

    private static object CreateRedactedConfigurationSnapshot(CodexplorerOptions options)
    {
        return new
        {
            options.Budget,
            options.Model,
            options.Workspace,
            options.Logging,
            OpenRouter = new
            {
                ApiKey = "***redacted***"
            }
        };
    }

    private sealed class OpenRouterHelloWorldRunner(IOptions<CodexplorerOptions> codexplorerOptions)
    {
        public async Task<int> RunAsync(CancellationToken cancellationToken = default)
        {
            var apiKey = codexplorerOptions.Value.OpenRouter.ApiKey;
            var modelName = codexplorerOptions.Value.Model.Name;
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
    }
}
