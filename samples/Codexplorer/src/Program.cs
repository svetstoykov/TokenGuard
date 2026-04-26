using Codexplorer.CLI;
using Codexplorer.Configuration;
using Codexplorer.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;

namespace Codexplorer;

internal sealed class Program
{
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

            using var host = builder.Build();
            await host.StartAsync().ConfigureAwait(false);

            try
            {
                LogResolvedConfiguration(host.Services);

                return await host.Services
                    .GetRequiredService<MainMenu>()
                    .RunAsync()
                    .ConfigureAwait(false);
            }
            finally
            {
                await host.StopAsync().ConfigureAwait(false);
            }
        }
        catch (OptionsValidationException ex)
        {
            foreach (var failure in ex.Failures)
            {
                await Console.Error.WriteLineAsync(failure).ConfigureAwait(false);
            }

            return 1;
        }
    }

    private static void LogResolvedConfiguration(IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        var options = services.GetRequiredService<IOptions<CodexplorerOptions>>().Value;
        var braveSearchSettings = services.GetRequiredService<BraveSearchSettings>();

        logger.LogDebug("Resolved Codexplorer configuration {@CodexplorerOptions}", CreateRedactedConfigurationSnapshot(options));

        if (!braveSearchSettings.IsConfigured)
        {
            logger.LogWarning(
                "BRAVE_SEARCH_API_KEY is not configured. web_search will return an error until a Brave Search API key is provided.");
        }
    }

    private static object CreateRedactedConfigurationSnapshot(CodexplorerOptions options)
    {
        return new
        {
            options.Budget,
            options.Model,
            options.Workspace,
            options.Agent,
            options.Logging,
            OpenRouter = new
            {
                ApiKey = "***redacted***"
            },
            BraveSearch = new
            {
                ApiKey = string.IsNullOrWhiteSpace(options.BraveSearch?.ApiKey) ? "(not configured)" : "***redacted***"
            }
        };
    }
}
