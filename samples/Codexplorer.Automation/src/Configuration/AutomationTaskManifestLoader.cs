using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Codexplorer.Automation.Configuration;

internal sealed class AutomationTaskManifestLoader : IAutomationTaskManifestLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly CodexplorerAutomationOptions _options;

    public AutomationTaskManifestLoader(IOptions<CodexplorerAutomationOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        this._options = options.Value;
    }

    public IReadOnlyList<AutomationTaskDefinition> LoadTasks()
    {
        if (!string.IsNullOrWhiteSpace(this._options.ManifestPath))
        {
            var resolvedManifestPath = AutomationPathResolver.ResolveFromCurrentDirectory(this._options.ManifestPath);
            using var stream = File.OpenRead(resolvedManifestPath);
            var manifest = JsonSerializer.Deserialize<AutomationTaskManifest>(stream, JsonOptions)
                ?? throw new InvalidOperationException($"Automation manifest '{resolvedManifestPath}' could not be deserialized.");

            return manifest.Tasks;
        }

        return this._options.Tasks;
    }
}
