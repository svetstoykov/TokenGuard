using System.Text.Json;
using Codexplorer.Automation.AgentComparison;
using Codexplorer.Automation.AgentComparison.OpenCode;
using Codexplorer.Automation.Configuration;
using FluentAssertions;

namespace Codexplorer.Automation.Tests.AgentComparison.OpenCode;

/// <summary>Verifies the comparison boundary and its observable outcomes.</summary>
public sealed class OpenCodeProcessConfigurationTests
{
    /// <summary>Verifies prepare isolates user state and denies autonomous and external tools.</summary>
    [Fact]
    public void Prepare_IsolatesUserStateAndDeniesAutonomousAndExternalTools()
    {
        var root = Path.Combine(Path.GetTempPath(), $"opencode-config-{Guid.NewGuid():N}");
        var checkout = Path.Combine(root, "checkout");
        var artifacts = Path.Combine(root, "artifacts");
        var stateRoot = Path.Combine(root, "state");
        Directory.CreateDirectory(checkout);
        try
        {
            var invocation = Invocation(checkout, artifacts, stateRoot);
            var launch = Prepare(invocation, checkout, artifacts, stateRoot);
            var configurationPath = launch.Environment["OPENCODE_CONFIG"];
            using var config = JsonDocument.Parse(File.ReadAllText(configurationPath));
            var rootElement = config.RootElement;
            var permissions = rootElement.GetProperty("permission");
            var external = permissions.GetProperty("external_directory");

            launch.Environment["HOME"].Should().Be(Path.Combine(stateRoot, "home"));
            launch.Environment["XDG_CONFIG_HOME"].Should().Be(Path.Combine(stateRoot, "xdg", "config"));
            launch.Environment["OPENCODE_DISABLE_PROJECT_CONFIG"].Should().Be("true");
            launch.Environment["OPENCODE_DISABLE_MODELS_FETCH"].Should().Be("true");
            launch.Environment["OPENCODE_DISABLE_AUTOUPDATE"].Should().Be("true");
            launch.Environment["OPENROUTER_API_KEY"].Should().Be(invocation.LocalCredential);
            launch.Environment.Should().NotContainKey("OPENAI_API_KEY");
            launch.Environment.Should().NotContainKey("ANTHROPIC_API_KEY");
            launch.Environment.Should().NotContainKey("GITHUB_TOKEN");
            launch.Environment.Should().NotContainKey("AWS_ACCESS_KEY_ID");
            launch.Environment.Should().NotContainKey("HTTP_PROXY");
            launch.Environment.Should().NotContainKey("COMSPEC");
            rootElement.GetProperty("enabled_providers").EnumerateArray().Select(value => value.GetString())
                .Should().Equal("openrouter");
            rootElement.GetProperty("small_model").GetString().Should().Be("openrouter/qwen/qwen3.7-flash");
            rootElement.GetProperty("autoupdate").GetBoolean().Should().BeFalse();
            rootElement.GetProperty("compaction").GetProperty("prune").GetBoolean().Should().BeFalse();
            rootElement.GetProperty("agent").GetProperty("title").GetProperty("model").GetString()
                .Should().Be("openrouter/qwen/qwen3.7-flash");
            rootElement.GetProperty("agent").GetProperty("compaction").GetProperty("model").GetString()
                .Should().Be("openrouter/qwen/qwen3.7-flash");
            var model = rootElement.GetProperty("provider").GetProperty("openrouter").GetProperty("models")
                .GetProperty("qwen/qwen3.7-flash");
            model.GetProperty("limit").GetProperty("context").GetInt32().Should().Be(1_000_000);
            model.GetProperty("limit").GetProperty("output").GetInt32().Should().Be(32_768);
            rootElement.GetProperty("plugin").GetArrayLength().Should().Be(0);
            rootElement.GetProperty("mcp").EnumerateObject().Should().BeEmpty();
            foreach (var tool in new[] { "bash", "task", "webfetch", "websearch", "skill", "question", "todowrite", "lsp" })
                permissions.GetProperty(tool).GetString().Should().Be("deny");
            permissions.GetProperty("*").GetString().Should().Be("deny");
            foreach (var tool in new[] { "read", "glob", "grep", "list", "edit", "write", "apply_patch" })
                permissions.GetProperty(tool).GetString().Should().Be("allow");
            external.GetProperty("*").GetString().Should().Be("deny");
            external.GetProperty(Path.Combine(artifacts, "**")).GetString().Should().Be("allow");
            var sessionRules = OpenCodeProcessConfiguration.CreateSessionPermissions(artifacts);
            sessionRules[0].Should().Be(new OpenCodeProcessConfiguration.OpenCodePermissionRule("*", "*", "deny"));
            sessionRules.Should().Contain(new OpenCodeProcessConfiguration.OpenCodePermissionRule("external_directory", "*", "deny"));
            sessionRules.Should().Contain(new OpenCodeProcessConfiguration.OpenCodePermissionRule(
                "external_directory", artifacts, "allow"));
            sessionRules.Should().Contain(new OpenCodeProcessConfiguration.OpenCodePermissionRule(
                "external_directory", Path.Combine(artifacts, "**"), "allow"));
            sessionRules.Should().OnlyContain(rule => rule.Action == "allow" || rule.Action == "deny");
            rootElement.GetProperty("provider").GetProperty("openrouter").GetProperty("options")
                .GetProperty("baseURL").GetString().Should().Be(invocation.RecorderEndpoint.AbsoluteUri);
            launch.Inventory.EffectivePolicy.Model.Should().Be("openrouter/qwen/qwen3.7-flash");
            launch.Inventory.DiscoveredFiles.Should().ContainSingle(file =>
                string.Equals(file.Path, launch.Inventory.EffectiveConfigPath, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Verifies prepare requires resolved provider context for live runtime.</summary>
    [Fact]
    public void Prepare_RequiresResolvedProviderContextForLiveRuntime()
    {
        var root = Path.Combine(Path.GetTempPath(), $"opencode-live-config-{Guid.NewGuid():N}");
        var checkout = Path.Combine(root, "checkout");
        var artifacts = Path.Combine(root, "artifacts");
        var stateRoot = Path.Combine(root, "state");
        Directory.CreateDirectory(checkout);
        try
        {
            var invocation = Invocation(checkout, artifacts, stateRoot) with
            {
                Synthetic = false,
                Settings = Invocation(checkout, artifacts, stateRoot).Settings with { ProviderContextTokens = null },
            };
            var act = () => Prepare(invocation, checkout, artifacts, stateRoot);
            act.Should().Throw<InvalidOperationException>().WithMessage("*resolved provider context limit*");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Verifies prepare inventories native instructions and configuration without following links.</summary>
    [Fact]
    public void Prepare_InventoriesNativeInstructionsAndConfigurationWithoutFollowingLinks()
    {
        var root = Path.Combine(Path.GetTempPath(), $"opencode-inventory-{Guid.NewGuid():N}");
        var checkout = Path.Combine(root, "checkout");
        var artifacts = Path.Combine(root, "artifacts");
        var stateRoot = Path.Combine(root, "state");
        Directory.CreateDirectory(Path.Combine(checkout, ".opencode", "plugins"));
        Directory.CreateDirectory(Path.Combine(checkout, ".git"));
        Directory.CreateDirectory(Path.Combine(checkout, ".artifacts"));
        try
        {
            File.WriteAllText(Path.Combine(checkout, "AGENTS.md"), "repo instructions");
            File.WriteAllText(Path.Combine(checkout, ".opencode", "plugins", "local.ts"), "plugin config");
            File.WriteAllText(Path.Combine(checkout, ".git", "AGENTS.md"), "ignored");
            File.WriteAllText(Path.Combine(checkout, ".artifacts", "AGENTS.md"), "ignored");

            var invocation = Invocation(checkout, artifacts, stateRoot);
            var launch = Prepare(invocation, checkout, artifacts, stateRoot);

            launch.Inventory.DiscoveredFiles.Select(file => Path.GetRelativePath(checkout, file.Path).Replace(Path.DirectorySeparatorChar, '/'))
                .Should().Contain("AGENTS.md")
                .And.Contain(".opencode/plugins/local.ts")
                .And.NotContain(".git/AGENTS.md")
                .And.NotContain(".artifacts/AGENTS.md");
            launch.Inventory.DiscoveredFiles.Should().OnlyContain(file => file.Sha256.Length == 64);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static RuntimeInvocation Invocation(string checkout, string artifacts, string stateRoot)
    {
        var task = new AutomationTaskDefinition { TaskId = "open-code-test", InitialPrompt = "Synthetic task" };
        var session = new ComparisonSessionPlan(
            "session-1", "small", 1, "opencode", task, "Synthetic task", "source", "adapted", 4, 1, null);
        return new RuntimeInvocation(
            session,
            "/path/to/opencode",
            checkout,
            artifacts,
            stateRoot,
            new Uri("http://127.0.0.1:43001/v1/"),
            "disposable-child-key",
            new ComparisonSettings("qwen/qwen3.7-flash", "openrouter", 65_536, true)
            {
                ProviderContextTokens = 1_000_000,
                ProviderMaxCompletionTokens = 32_768,
            },
            Synthetic: true);
    }

    private static OpenCodeProcessConfiguration.OpenCodeLaunchConfiguration Prepare(
        RuntimeInvocation invocation,
        string checkout,
        string artifacts,
        string stateRoot) => OpenCodeProcessConfiguration.Prepare(
            invocation,
            checkout,
            artifacts,
            stateRoot,
            invocation.RecorderEndpoint,
            invocation.LocalCredential);
}
