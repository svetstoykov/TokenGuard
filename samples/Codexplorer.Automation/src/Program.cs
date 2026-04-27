using Codexplorer.Automation.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Codexplorer.Automation;

internal sealed class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var builder = Host.CreateApplicationBuilder(args);
            builder.Configuration.AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false);
            builder.Services.AddCodexplorerAutomation(builder.Configuration);

            using var host = builder.Build();
            await host.StartAsync().ConfigureAwait(false);

            try
            {
                return await host.Services
                    .GetRequiredService<AutomationRunner>()
                    .RunAsync(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping)
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
}
