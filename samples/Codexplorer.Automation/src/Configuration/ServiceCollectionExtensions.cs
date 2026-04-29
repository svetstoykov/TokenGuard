using Codexplorer.Automation.Client;
using Codexplorer.Automation.Runner;
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
    public static IServiceCollection AddCodexplorerAutomation(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<CodexplorerAutomationOptions>, CodexplorerAutomationOptionsValidator>());

        services.AddOptions<CodexplorerAutomationOptions>()
            .Bind(configuration.GetSection(CodexplorerAutomationOptions.SectionName))
            .ValidateOnStart();

        services.TryAddSingleton<IAutomationProtocolTransport, ProcessAutomationProtocolTransport>();
        services.TryAddSingleton<ICodexplorerAutomationClient, CodexplorerAutomationClient>();
        services.TryAddSingleton<IAutomationTaskManifestLoader, AutomationTaskManifestLoader>();
        services.TryAddSingleton<IRunnerHelperAi, OpenRouterRunnerHelperAi>();
        services.TryAddSingleton<AutomationRunner>();

        return services;
    }
}
