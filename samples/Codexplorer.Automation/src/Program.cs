using Codexplorer.Automation.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;

namespace Codexplorer.Automation;

internal sealed class Program
{
    public static async Task<int> Main(string[] args)
    {
        var logFilePath = Path.Combine(AppContext.BaseDirectory, "logs", "codexplorer-automation-.log");
        Log.Logger = CreateLogger(logFilePath);

        try
        {
            Log.Information("Starting Codexplorer.Automation.");

            var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings
            {
                Args = args,
                ContentRootPath = AppContext.BaseDirectory
            });
            builder.Configuration.AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false);
            builder.Services.AddSerilog();
            builder.Services.AddCodexplorerAutomation(builder.Configuration);

            using var host = builder.Build();
            await host.StartAsync().ConfigureAwait(false);

            try
            {
                LogResolvedConfiguration(host.Services, logFilePath);

                return await host.Services
                    .GetRequiredService<AutomationRunner>()
                    .RunAsync(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping)
                    .ConfigureAwait(false);
            }
            finally
            {
                await host.StopAsync().ConfigureAwait(false);
                Log.Information("Stopped Codexplorer.Automation host.");
            }
        }
        catch (OptionsValidationException ex)
        {
            foreach (var failure in ex.Failures)
            {
                await Console.Error.WriteLineAsync(failure).ConfigureAwait(false);
            }

            Log.Error(ex, "Codexplorer.Automation failed validation during startup.");
            return 1;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Codexplorer.Automation terminated unexpectedly.");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync().ConfigureAwait(false);
        }
    }

    private static void LogResolvedConfiguration(IServiceProvider services, string logFilePath)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        var options = services.GetRequiredService<IOptions<CodexplorerAutomationOptions>>().Value;

        logger.LogInformation(
            "Resolved automation configuration. ManifestPath={ManifestPath}, InlineTaskCount={InlineTaskCount}, ExecutablePath={ExecutablePath}, HelperModel={HelperModel}, LogFilePath={LogFilePath}.",
            options.ManifestPath,
            options.Tasks.Count,
            options.CodexplorerExecutablePath,
            options.HelperAi.ModelName,
            logFilePath);
    }

    private static Serilog.ILogger CreateLogger(string logFilePath)
    {
        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                logFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }
}
