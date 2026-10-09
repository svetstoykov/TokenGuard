using Codexplorer.Automation.Client;
using Codexplorer.Automation.Scoring;
using Codexplorer.Automation.Runner;
using Codexplorer.Automation.Reporting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Codexplorer.Automation.Configuration;

/// <summary>
/// Registers configuration and automation runner services.
/// </summary>
/// <remarks>
/// The automation runner stays decoupled from Codexplorer runtime internals by resolving only its own
/// configuration, transport, and typed protocol client services. The child Codexplorer process remains
/// the sole owner of session orchestration and model execution.
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Binds and validates automation runner configuration, then registers process and protocol services.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <param name="configuration">The configuration root used to bind runner options.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance for fluent chaining.</returns>
    /// <remarks>
    /// When helper credentials are absent, reads Codexplorer:OpenRouter:ApiKey from appsettings.Development.json beside the sample executable.
    /// </remarks>
    public static IServiceCollection AddCodexplorerAutomation(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var helperApiKeyPath = $"{CodexplorerAutomationOptions.SectionName}:HelperAi:ApiKey";
        var executablePath = configuration[$"{CodexplorerAutomationOptions.SectionName}:CodexplorerExecutablePath"];
        if (string.IsNullOrWhiteSpace(configuration[helperApiKeyPath]) && string.IsNullOrWhiteSpace(configuration["OPENROUTER_API_KEY"])
            && !string.IsNullOrWhiteSpace(executablePath) && Path.IsPathRooted(executablePath))
        {
            using var sampleConfiguration = new ConfigurationManager();
            sampleConfiguration.AddJsonFile(Path.Combine(Path.GetDirectoryName(executablePath)!, "appsettings.Development.json"), optional: true);
            var sampleApiKey = sampleConfiguration["Codexplorer:OpenRouter:ApiKey"];
            if (!string.IsNullOrWhiteSpace(sampleApiKey))
                configuration[helperApiKeyPath] = sampleApiKey;
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<CodexplorerAutomationOptions>, CodexplorerAutomationOptionsValidator>());

        services.AddOptions<CodexplorerAutomationOptions>()
            .Bind(configuration.GetSection(CodexplorerAutomationOptions.SectionName))
            .ValidateOnStart();

        services.TryAddSingleton<IAutomationProtocolTransport, ProcessAutomationProtocolTransport>();
        services.TryAddSingleton<ICodexplorerAutomationClient, CodexplorerAutomationClient>();
        services.TryAddSingleton<IAnswerScorer, AnswerScorer>();
        services.TryAddSingleton<IAutomationTaskManifestLoader, AutomationTaskManifestLoader>();
        services.TryAddSingleton<IRunnerHelperAi, OpenRouterRunnerHelperAi>();
        services.TryAddSingleton<IRepositoryIdentityReader, GitRepositoryIdentityReader>();
        services.TryAddSingleton<IReportAggregator, ReportAggregator>();
        services.TryAddSingleton<IRunReportWriter, JsonRunReportWriter>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<AutomationRunner>();

        return services;
    }
}
