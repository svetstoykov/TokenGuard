using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Codexplorer.Automation.AgentComparison.Recording;

namespace Codexplorer.Automation.AgentComparison.OpenCode;

/// <summary>Builds isolated OpenCode configuration and records discovered instruction provenance.</summary>
internal static class OpenCodeProcessConfiguration
{
    /// <summary>Creates isolated native configuration with explicit model routing and tool restrictions.</summary>
    /// <param name="invocation">The source session and enforced provider settings.</param>
    /// <param name="checkout">The isolated repository checkout.</param>
    /// <param name="artifacts">The exact authorized external artifact directory.</param>
    /// <param name="stateRoot">The isolated runtime state directory.</param>
    /// <param name="recorderEndpoint">The loopback recorder endpoint.</param>
    /// <param name="localCredential">The disposable child credential.</param>
    /// <returns>The redacted provenance inventory and isolated child environment.</returns>
    public static OpenCodeLaunchConfiguration Prepare(
        RuntimeInvocation invocation,
        string checkout,
        string artifacts,
        string stateRoot,
        Uri recorderEndpoint,
        string localCredential)
    {
        var home = Path.Combine(stateRoot, "home");
        var configRoot = Path.Combine(stateRoot, "xdg", "config");
        var dataRoot = Path.Combine(stateRoot, "xdg", "data");
        var statePath = Path.Combine(stateRoot, "xdg", "state");
        var cacheRoot = Path.Combine(stateRoot, "xdg", "cache");
        var temporaryRoot = Path.Combine(stateRoot, "tmp");
        var isolatedConfig = Path.Combine(configRoot, "opencode.json");
        Directory.CreateDirectory(home);
        Directory.CreateDirectory(configRoot);
        Directory.CreateDirectory(dataRoot);
        Directory.CreateDirectory(statePath);
        Directory.CreateDirectory(cacheRoot);
        Directory.CreateDirectory(temporaryRoot);
        Directory.CreateDirectory(artifacts);

        var config = CreateConfig(invocation, artifacts, recorderEndpoint);
        File.WriteAllText(isolatedConfig, JsonSerializer.Serialize(
            config,
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        RestrictFilePermissions(isolatedConfig);

        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["HOME"] = home,
            ["XDG_CONFIG_HOME"] = configRoot,
            ["XDG_DATA_HOME"] = dataRoot,
            ["XDG_STATE_HOME"] = statePath,
            ["XDG_CACHE_HOME"] = cacheRoot,
            ["TMPDIR"] = temporaryRoot,
            ["OPENCODE_CONFIG"] = isolatedConfig,
            ["OPENCODE_CONFIG_DIR"] = Path.Combine(configRoot, "opencode"),
            ["OPENCODE_DISABLE_PROJECT_CONFIG"] = "true",
            ["OPENCODE_DISABLE_MODELS_FETCH"] = "true",
            ["OPENCODE_DISABLE_AUTOUPDATE"] = "true",
            ["OPENROUTER_API_KEY"] = localCredential,
            ["PATH"] = OperatingSystem.IsWindows()
                ? Environment.GetFolderPath(Environment.SpecialFolder.System)
                : "/usr/bin:/bin:/usr/sbin:/sbin",
            ["LANG"] = "en_US.UTF-8",
        };
        AddEnvironmentIfPresent(environment, "SYSTEMROOT");
        AddEnvironmentIfPresent(environment, "WINDIR");

        var inventory = DiscoverInventory(checkout, stateRoot, isolatedConfig, config);
        return new OpenCodeLaunchConfiguration(environment, inventory);
    }

    private static OpenCodeRuntimeConfig CreateConfig(RuntimeInvocation invocation, string artifacts, Uri recorderEndpoint)
    {
        var contextTokens = invocation.Settings.ProviderContextTokens
            ?? (invocation.Synthetic
                ? invocation.Session.ContextWindowTokens ?? 1_000_000
                : throw new InvalidOperationException("OpenCode requires the resolved provider context limit."));
        var outputTokens = invocation.Settings.ProviderMaxCompletionTokens is int providerMaximum
            ? Math.Min(invocation.Settings.MaxOutputTokens, providerMaximum)
            : invocation.Settings.MaxOutputTokens;
        if (contextTokens <= 0 || outputTokens <= 0)
            throw new InvalidOperationException("OpenCode model limits must be positive.");
        var allowedArtifactPattern = Path.Combine(artifacts, "**");
        return new OpenCodeRuntimeConfig(
            "https://opencode.ai/config.json",
            $"openrouter/{invocation.Settings.Model}",
            $"openrouter/{invocation.Settings.Model}",
            ["openrouter"],
            false,
            "disabled",
            [],
            new Dictionary<string, bool>
            {
                ["bash"] = false,
                ["task"] = false,
                ["webfetch"] = false,
                ["websearch"] = false,
                ["skill"] = false,
                ["question"] = false,
                ["todowrite"] = false,
                ["lsp"] = false,
            },
            new Dictionary<string, OpenCodeProviderConfig>(StringComparer.Ordinal)
            {
                ["openrouter"] = new OpenCodeProviderConfig(
                    "TokenGuard recorder",
                    new Dictionary<string, string> { ["baseURL"] = recorderEndpoint.AbsoluteUri },
                    new Dictionary<string, OpenCodeModelConfig>(StringComparer.Ordinal)
                    {
                        [invocation.Settings.Model] = new OpenCodeModelConfig(
                            invocation.Settings.Model,
                            new OpenCodeModelLimit(contextTokens, outputTokens),
                            new Dictionary<string, JsonElement>
                            {
                                ["reasoning"] = JsonSerializer.SerializeToElement(new
                                {
                                    enabled = invocation.Settings.ReasoningEnabled,
                                }),
                            }),
                    }),
            },
            new Dictionary<string, object>
            {
                ["*"] = "deny",
                ["read"] = "allow",
                ["glob"] = "allow",
                ["grep"] = "allow",
                ["list"] = "allow",
                ["edit"] = "allow",
                ["write"] = "allow",
                ["apply_patch"] = "allow",
                ["bash"] = "deny",
                ["task"] = "deny",
                ["webfetch"] = "deny",
                ["websearch"] = "deny",
                ["skill"] = "deny",
                ["question"] = "deny",
                ["todowrite"] = "deny",
                ["lsp"] = "deny",
                ["external_directory"] = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["*"] = "deny",
                    [allowedArtifactPattern] = "allow",
                },
            },
            new Dictionary<string, object> { ["auto"] = true, ["prune"] = false },
            new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["title"] = new { model = $"openrouter/{invocation.Settings.Model}" },
                ["compaction"] = new { model = $"openrouter/{invocation.Settings.Model}" },
            },
            new Dictionary<string, bool>());
    }

    /// <summary>Creates fail-closed session permissions that override agent defaults.</summary>
    public static IReadOnlyList<OpenCodePermissionRule> CreateSessionPermissions(string artifacts)
    {
        var rules = new List<OpenCodePermissionRule>
        {
            new("*", "*", "deny"),
            new("read", "*", "allow"),
            new("glob", "*", "allow"),
            new("grep", "*", "allow"),
            new("list", "*", "allow"),
            new("edit", "*", "allow"),
            new("write", "*", "allow"),
            new("apply_patch", "*", "allow"),
            new("external_directory", "*", "deny"),
            new("external_directory", artifacts, "allow"),
            new("external_directory", Path.Combine(artifacts, "**"), "allow"),
        };
        return rules;
    }

    private static OpenCodeInstructionInventory DiscoverInventory(
        string checkout,
        string stateRoot,
        string isolatedConfig,
        OpenCodeRuntimeConfig effectiveConfig)
    {
        var candidates = new HashSet<string>(PathComparer);
        var instructionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AGENTS.md",
            "CLAUDE.md",
            "GEMINI.md",
            "opencode.json",
            "opencode.jsonc",
        };
        var ignoredDirectories = new HashSet<string>(StringComparer.Ordinal)
        {
            ".git", "bin", "obj", "node_modules", ".artifacts", ".tasks", ".specs",
        };
        var pending = new Stack<string>();
        pending.Push(checkout);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                if (instructionNames.Contains(Path.GetFileName(file)))
                    candidates.Add(Path.GetFullPath(file));
            }

            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                if (ignoredDirectories.Contains(Path.GetFileName(child)))
                    continue;
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                    continue;
                if (Path.GetFileName(child) is ".opencode" or ".agents" or ".claude" or ".codex")
                {
                    foreach (var file in EnumerateFilesWithoutLinks(child))
                        candidates.Add(Path.GetFullPath(file));
                    continue;
                }
                pending.Push(child);
            }
        }

        foreach (var file in Directory.EnumerateFiles(stateRoot, "*", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(file) is "opencode.json" or "opencode.jsonc" or "config.json")
                candidates.Add(Path.GetFullPath(file));
        }

        var files = candidates
            .OrderBy(path => path, PathComparer)
            .Select(path => new OpenCodePathDigest(path, HashFile(path)))
            .ToArray();
        return new OpenCodeInstructionInventory(
            "1.19.3",
            checkout,
            stateRoot,
            isolatedConfig,
            files,
            new OpenCodeEffectivePolicy(
                effectiveConfig.Model,
                effectiveConfig.Provider["openrouter"].Options["baseURL"],
                effectiveConfig.Permission,
                effectiveConfig.Plugin,
                effectiveConfig.Mcp,
                effectiveConfig.Tools));
    }

    private static IEnumerable<string> EnumerateFilesWithoutLinks(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0)
                    yield return file;
            }
            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                    pending.Push(child);
            }
        }
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void AddEnvironmentIfPresent(IDictionary<string, string> target, string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrWhiteSpace(value))
            target[name] = value;
    }

    private static void RestrictFilePermissions(string path)
    {
        if (OperatingSystem.IsWindows())
            return;
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    internal sealed record OpenCodeLaunchConfiguration(
        IReadOnlyDictionary<string, string> Environment,
        OpenCodeInstructionInventory Inventory);

    internal sealed record OpenCodeInstructionInventory(
        string RuntimeVersion,
        string Checkout,
        string IsolatedStateRoot,
        string EffectiveConfigPath,
        IReadOnlyList<OpenCodePathDigest> DiscoveredFiles,
        OpenCodeEffectivePolicy EffectivePolicy);

    internal sealed record OpenCodePathDigest(string Path, string Sha256);

    internal sealed record OpenCodeEffectivePolicy(
        string Model,
        string BaseUrl,
        IReadOnlyDictionary<string, object> Permissions,
        IReadOnlyList<string> Plugins,
        IReadOnlyDictionary<string, bool> Mcp,
        IReadOnlyDictionary<string, bool> Tools);

    internal sealed record OpenCodeRuntimeConfig(
        [property: JsonPropertyName("$schema")] string Schema,
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("small_model")] string SmallModel,
        [property: JsonPropertyName("enabled_providers")] IReadOnlyList<string> EnabledProviders,
        [property: JsonPropertyName("autoupdate")] bool AutoUpdate,
        string Share,
        IReadOnlyList<string> Plugin,
        IReadOnlyDictionary<string, bool> Tools,
        IReadOnlyDictionary<string, OpenCodeProviderConfig> Provider,
        IReadOnlyDictionary<string, object> Permission,
        IReadOnlyDictionary<string, object> Compaction,
        IReadOnlyDictionary<string, object> Agent,
        IReadOnlyDictionary<string, bool> Mcp);

    internal sealed record OpenCodeProviderConfig(
        string Name,
        IReadOnlyDictionary<string, string> Options,
        IReadOnlyDictionary<string, OpenCodeModelConfig> Models);

    internal sealed record OpenCodeModelConfig(
        string Name,
        OpenCodeModelLimit Limit,
        IReadOnlyDictionary<string, JsonElement> Options);

    internal sealed record OpenCodeModelLimit(int Context, int Output);

    /// <summary>A native OpenCode session permission rule.</summary>
    public sealed record OpenCodePermissionRule(string Permission, string Pattern, string Action);
}
