using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Codexplorer.Automation.Configuration;

internal sealed class AutomationTaskManifestLoader : IAutomationTaskManifestLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly CodexplorerAutomationOptions _options;
    private readonly ILogger<AutomationTaskManifestLoader> _logger;

    public AutomationTaskManifestLoader(
        IOptions<CodexplorerAutomationOptions> options,
        ILogger<AutomationTaskManifestLoader> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        this._options = options.Value;
        this._logger = logger;
    }

    public IReadOnlyList<AutomationTaskDefinition> LoadTasks()
    {
        if (!string.IsNullOrWhiteSpace(this._options.ManifestPath))
        {
            var resolvedManifestPath = Path.GetFullPath(this._options.ManifestPath, AppContext.BaseDirectory);
            this._logger.LogInformation("Loading automation tasks from manifest {ManifestPath}.", resolvedManifestPath);
            using var stream = File.OpenRead(resolvedManifestPath);
            var manifest = JsonSerializer.Deserialize<AutomationTaskManifest>(stream, JsonOptions)
                ?? throw new InvalidOperationException($"Automation manifest '{resolvedManifestPath}' could not be deserialized.");

            this._logger.LogInformation(
                "Loaded {TaskCount} automation tasks from manifest {ManifestPath}.",
                manifest.Tasks.Count,
                resolvedManifestPath);
            return manifest.Tasks;
        }

        this._logger.LogInformation(
            "Using {TaskCount} automation tasks from inline configuration.",
            this._options.Tasks.Count);
        return this._options.Tasks;
    }
}
