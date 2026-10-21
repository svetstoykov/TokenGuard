using System.Text.Json;

namespace Codexplorer.Automation.AgentComparison.Recording;

/// <summary>Resolves an OpenRouter key from the environment or the original sample development settings.</summary>
internal sealed class ComparisonCredentialResolver : IComparisonCredentialResolver
{
    private readonly Func<string?> _environmentKey;
    private readonly string _developmentSettingsPath;

    /// <summary>Initializes a resolver for the original Codexplorer executable.</summary>
    /// <param name="originalExecutablePath">The original sample executable path.</param>
    public ComparisonCredentialResolver(string originalExecutablePath)
        : this(() => Environment.GetEnvironmentVariable("OPENROUTER_API_KEY"),
            Path.Combine(Path.GetDirectoryName(Path.GetFullPath(originalExecutablePath))!, "appsettings.Development.json"))
    {
    }

    /// <summary>Initializes a resolver with injectable local credential sources.</summary>
    /// <param name="environmentKey">A function that returns the environment credential.</param>
    /// <param name="developmentSettingsPath">The ignored development settings path.</param>
    internal ComparisonCredentialResolver(Func<string?> environmentKey, string developmentSettingsPath)
    {
        this._environmentKey = environmentKey;
        this._developmentSettingsPath = developmentSettingsPath;
    }

    /// <inheritdoc />
    public ResolvedComparisonCredential Resolve()
    {
        var environmentKey = this._environmentKey();
        if (!string.IsNullOrWhiteSpace(environmentKey))
            return new ResolvedComparisonCredential(environmentKey, "OPENROUTER_API_KEY", true);

        var sampleKey = this.ReadDevelopmentKey();
        return string.IsNullOrWhiteSpace(sampleKey)
            ? new ResolvedComparisonCredential(null, "none", false)
            : new ResolvedComparisonCredential(sampleKey, "appsettings.Development.json", true);
    }

    /// <inheritdoc />
    public CredentialPresence Inspect()
    {
        var resolved = this.Resolve();
        return new CredentialPresence(resolved.Present, resolved.Source);
    }

    private string? ReadDevelopmentKey()
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(this._developmentSettingsPath));
            return document.RootElement.TryGetProperty("Codexplorer", out var codexplorer)
                && codexplorer.TryGetProperty("OpenRouter", out var openRouter)
                && openRouter.TryGetProperty("ApiKey", out var apiKey)
                && apiKey.ValueKind == JsonValueKind.String
                    ? apiKey.GetString()
                    : null;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
