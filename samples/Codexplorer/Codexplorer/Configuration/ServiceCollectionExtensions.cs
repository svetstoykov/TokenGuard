using Codexplorer.Agent;
using Codexplorer.ConsoleRendering;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Codexplorer.Sessions;
using Codexplorer.Tools;
using Codexplorer.Workspace;
using TokenGuard.Core.Extensions;

namespace Codexplorer.Configuration;

/// <summary>
/// Registers Codexplorer configuration binding and startup validation services.
/// </summary>
/// <remarks>
/// <para>
/// This extension centralizes Codexplorer startup configuration so later features resolve one
/// validated <see cref="CodexplorerOptions"/> snapshot instead of reading raw configuration keys.
/// </para>
/// <para>
/// Startup validation checks the full bound <see cref="CodexplorerOptions"/> graph, including the
/// local-development OpenRouter settings required for authenticated sample calls.
/// </para>
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Binds <see cref="CodexplorerOptions"/> from configuration and validates the result on startup.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <param name="configuration">The application configuration root used for section binding.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// The <c>Codexplorer</c> section is bound once during startup and exposed through the standard
    /// options abstractions. Validation runs during host startup so invalid configuration fails fast
    /// before Codexplorer begins any workspace or model operations.
    /// </para>
    /// <para>
    /// This method expects local developer secrets such as <c>Codexplorer:OpenRouter:ApiKey</c> to
    /// arrive through configuration providers like <c>appsettings.Development.json</c>, while shared
    /// defaults continue to live in <c>appsettings.json</c>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="configuration"/> is <see langword="null"/>.
    /// </exception>
    public static IServiceCollection AddCodexplorerOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<CodexplorerOptions>, CodexplorerOptionsValidator>());

        services.AddOptions<CodexplorerOptions>()
            .Bind(configuration.GetSection(CodexplorerOptions.SectionName))
            .ValidateOnStart();

        services.AddConversationContext(builder =>
        {
            var budgetOptions = configuration.GetSection(CodexplorerOptions.SectionName)
                .Get<CodexplorerOptions>()?.Budget ?? new BudgetOptions();

            builder
                .WithMaxTokens(budgetOptions.ContextWindowTokens)
                .WithCompactionThreshold(budgetOptions.SoftThresholdRatio)
                .WithEmergencyThreshold(budgetOptions.HardThresholdRatio);
        });

        services.TryAddSingleton<IGitCloner, LibGit2Cloner>();
        services.TryAddSingleton<IWorkspaceManager, WorkspaceManager>();
        services.TryAddSingleton<IToolRegistry, ToolRegistry>();
        services.TryAddSingleton<ISessionLoggerFactory, SessionLoggerFactory>();
        services.TryAddSingleton<SessionRenderer>();
        services.TryAddSingleton<IExplorerAgent, ExplorerAgent>();

        return services;
    }
}
