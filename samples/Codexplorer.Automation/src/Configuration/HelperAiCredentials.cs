using Microsoft.Extensions.Configuration;

namespace Codexplorer.Automation.Configuration;

/// <summary>Provides the helper credential precedence shared by validation and construction.</summary>
internal static class HelperAiCredentials
{
    /// <summary>Resolves the effective helper credential.</summary>
    /// <param name="configuration">The configuration containing the environment credential.</param>
    /// <param name="options">The configured helper settings.</param>
    /// <returns>The environment credential when nonempty, otherwise the configured credential.</returns>
    internal static string? Resolve(IConfiguration configuration, AutomationHelperAiOptions options)
    {
        var environmentKey = configuration["OPENROUTER_API_KEY"];
        return string.IsNullOrWhiteSpace(environmentKey) ? options.ApiKey : environmentKey;
    }
}
