using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Codexplorer.Automation.Configuration;

/// <summary>Loads validated manifest tasks and hashes the exact bytes once.</summary>
internal sealed class AutomationTaskManifestLoader : IAutomationTaskManifestLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly Lock _sync = new();
    private AutomationManifestSnapshot? _snapshot;
    private readonly CodexplorerAutomationOptions _options;
    private readonly ILogger<AutomationTaskManifestLoader> _logger;

    /// <summary>Initializes a new instance of the <see cref="AutomationTaskManifestLoader" /> class.</summary>
    /// <param name="options">The runner configuration.</param>
    /// <param name="logger">The manifest-load logger.</param>
    public AutomationTaskManifestLoader(
        IOptions<CodexplorerAutomationOptions> options,
        ILogger<AutomationTaskManifestLoader> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        this._options = options.Value;
        this._logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<AutomationTaskDefinition> LoadTasks() => this.LoadSnapshot().Tasks;

    /// <inheritdoc />
    public AutomationManifestSnapshot LoadSnapshot()
    {
        lock (this._sync)
        {
            if (this._snapshot is not null)
            {
                return this._snapshot;
            }

            var fromFile = !string.IsNullOrWhiteSpace(this._options.ManifestPath);
            var path = fromFile ? Path.GetFullPath(this._options.ManifestPath!, AppContext.BaseDirectory) : "inline";
            byte[] bytes;
            AutomationTaskManifest manifest;
            try
            {
                bytes = fromFile
                    ? File.ReadAllBytes(path)
                    : JsonSerializer.SerializeToUtf8Bytes(new AutomationTaskManifest { Tasks = this._options.Tasks }, JsonOptions);
                var offset = bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? 3 : 0;
                manifest = JsonSerializer.Deserialize<AutomationTaskManifest>(bytes.AsSpan(offset), JsonOptions)
                    ?? throw new OptionsValidationException(CodexplorerAutomationOptions.SectionName, typeof(AutomationTaskManifest),
                        ["The automation manifest is empty."]);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                var failure = ex switch
                {
                    FileNotFoundException or DirectoryNotFoundException => $"Configured manifest path '{path}' does not exist.",
                    JsonException => $"Configured manifest path '{path}' contains invalid JSON.",
                    _ => $"Configured manifest path '{path}' could not be read."
                };
                throw new OptionsValidationException(CodexplorerAutomationOptions.SectionName, typeof(AutomationTaskManifest), [failure]);
            }

            var configuredTasks = manifest.Tasks ?? [];
            var failures = new List<string>();
            CodexplorerAutomationOptionsValidator.ValidateTasks(configuredTasks, this._options.ManifestPath, failures);
            if (failures.Count > 0)
            {
                throw new OptionsValidationException(CodexplorerAutomationOptions.SectionName, typeof(AutomationTaskManifest), failures);
            }

            var tasks = Array.AsReadOnly(configuredTasks.ToArray());
            this._snapshot = new AutomationManifestSnapshot(tasks, path, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                fromFile ? "file" : "inline");
            this._logger.LogInformation("Loaded {TaskCount} automation tasks from {Provenance} manifest.", tasks.Count, this._snapshot.Provenance);
            return this._snapshot;
        }
    }
}
