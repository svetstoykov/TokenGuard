using Codexplorer.App.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Codexplorer.App.Tests.Configuration;

[Collection(CodexplorerConfigurationCollection.Name)]
public sealed class CodexplorerOptionsValidatorTests
{
    [Fact]
    public async Task AddCodexplorerOptions_BindsConfiguredValues()
    {
        var settings = CreateValidSettings();
        settings["Codexplorer:Budget:ContextWindowTokens"] = "32000";
        settings["Codexplorer:Budget:SoftThresholdRatio"] = "0.6";
        settings["Codexplorer:Budget:HardThresholdRatio"] = "0.85";
        settings["Codexplorer:Budget:WindowSize"] = "8";
        settings["Codexplorer:Model:Name"] = "openai/gpt-5-mini";
        settings["Codexplorer:Model:MaxOutputTokens"] = "4096";
        settings["Codexplorer:Model:Temperature"] = "0.25";
        settings["Codexplorer:Workspace:RootDirectory"] = "./repos";
        settings["Codexplorer:Workspace:CloneDepth"] = "2";
        settings["Codexplorer:Workspace:MaxRepoSizeMB"] = "250";
        settings["Codexplorer:Logging:SessionLogsDirectory"] = "./var/logs";
        settings["Codexplorer:Logging:MinimumLevel"] = "Debug";
        settings["Codexplorer:OpenRouter:ApiKey"] = "custom-test-api-key";

        using var host = BuildHost(settings);
        await host.StartAsync();

        var options = host.Services.GetRequiredService<IOptions<CodexplorerOptions>>().Value;

        options.Budget.ContextWindowTokens.Should().Be(32_000);
        options.Budget.SoftThresholdRatio.Should().Be(0.6);
        options.Budget.HardThresholdRatio.Should().Be(0.85);
        options.Budget.WindowSize.Should().Be(8);
        options.Model.Name.Should().Be("openai/gpt-5-mini");
        options.Model.MaxOutputTokens.Should().Be(4_096);
        options.Model.Temperature.Should().Be(0.25);
        options.Workspace.RootDirectory.Should().Be("./repos");
        options.Workspace.CloneDepth.Should().Be(2);
        options.Workspace.MaxRepoSizeMB.Should().Be(250);
        options.Logging.SessionLogsDirectory.Should().Be("./var/logs");
        options.Logging.MinimumLevel.Should().Be("Debug");
        options.OpenRouter.ApiKey.Should().Be("custom-test-api-key");
    }

    [Fact]
    public async Task AddCodexplorerOptions_WhenSubsectionsAreOmitted_KeepsDefaultValues()
    {
        Dictionary<string, string?> settings = new()
        {
            ["Codexplorer:Model:Name"] = "openai/gpt-5-mini",
            ["Codexplorer:OpenRouter:ApiKey"] = "test-api-key"
        };

        using var host = BuildHost(settings);
        await host.StartAsync();

        var options = host.Services.GetRequiredService<IOptions<CodexplorerOptions>>().Value;

        options.Budget.ContextWindowTokens.Should().Be(16_000);
        options.Budget.SoftThresholdRatio.Should().Be(0.70);
        options.Budget.HardThresholdRatio.Should().Be(0.90);
        options.Budget.WindowSize.Should().Be(5);
        options.Model.Name.Should().Be("openai/gpt-5-mini");
        options.Model.MaxOutputTokens.Should().Be(2_048);
        options.Model.Temperature.Should().Be(0.0);
        options.Workspace.RootDirectory.Should().Be("./workspace");
        options.Workspace.CloneDepth.Should().Be(1);
        options.Workspace.MaxRepoSizeMB.Should().Be(500);
        options.Logging.SessionLogsDirectory.Should().Be("./logs/sessions");
        options.Logging.MinimumLevel.Should().Be("Information");
        options.OpenRouter.ApiKey.Should().Be("test-api-key");
    }

    [Theory]
    [InlineData("Codexplorer:Budget:ContextWindowTokens", "-1", "Codexplorer:Budget:ContextWindowTokens must be greater than or equal to 0.")]
    [InlineData("Codexplorer:Budget:WindowSize", "-1", "Codexplorer:Budget:WindowSize must be greater than or equal to 0.")]
    [InlineData("Codexplorer:Model:MaxOutputTokens", "-1", "Codexplorer:Model:MaxOutputTokens must be greater than or equal to 0.")]
    [InlineData("Codexplorer:Workspace:CloneDepth", "-1", "Codexplorer:Workspace:CloneDepth must be greater than or equal to 0.")]
    [InlineData("Codexplorer:Workspace:MaxRepoSizeMB", "-1", "Codexplorer:Workspace:MaxRepoSizeMB must be greater than or equal to 0.")]
    public async Task AddCodexplorerOptions_WhenIntegerValueIsNegative_FailsStartup(
        string key,
        string value,
        string expectedMessage)
    {
        var settings = CreateValidSettings();
        settings[key] = value;

        using var host = BuildHost(settings);
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        exception.Failures.Should().ContainSingle().Which.Should().Be(expectedMessage);
    }

    [Theory]
    [InlineData("Codexplorer:Budget:SoftThresholdRatio", "0.0", "Codexplorer:Budget:SoftThresholdRatio must be in range (0, 1].")]
    [InlineData("Codexplorer:Budget:SoftThresholdRatio", "1.1", "Codexplorer:Budget:SoftThresholdRatio must be in range (0, 1].")]
    [InlineData("Codexplorer:Budget:HardThresholdRatio", "0.0", "Codexplorer:Budget:HardThresholdRatio must be in range (0, 1].")]
    [InlineData("Codexplorer:Budget:HardThresholdRatio", "1.1", "Codexplorer:Budget:HardThresholdRatio must be in range (0, 1].")]
    public async Task AddCodexplorerOptions_WhenRatioIsOutsideAllowedRange_FailsStartup(
        string key,
        string value,
        string expectedMessage)
    {
        var settings = CreateValidSettings();
        settings[key] = value;

        using var host = BuildHost(settings);
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        exception.Failures.Should().ContainSingle().Which.Should().Be(expectedMessage);
    }

    [Fact]
    public async Task AddCodexplorerOptions_WhenHardThresholdIsNotGreaterThanSoftThreshold_FailsStartup()
    {
        var settings = CreateValidSettings();
        settings["Codexplorer:Budget:SoftThresholdRatio"] = "0.9";
        settings["Codexplorer:Budget:HardThresholdRatio"] = "0.9";

        using var host = BuildHost(settings);
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        exception.Failures.Should()
            .ContainSingle()
            .Which.Should().Be(
                "Codexplorer:Budget:HardThresholdRatio must be greater than Codexplorer:Budget:SoftThresholdRatio.");
    }

    [Theory]
    [InlineData("Codexplorer:Model:Name", "   ", "Codexplorer:Model:Name cannot be empty or whitespace.")]
    [InlineData("Codexplorer:Workspace:RootDirectory", "   ", "Codexplorer:Workspace:RootDirectory cannot be empty or whitespace.")]
    [InlineData("Codexplorer:Logging:SessionLogsDirectory", "   ", "Codexplorer:Logging:SessionLogsDirectory cannot be empty or whitespace.")]
    [InlineData("Codexplorer:OpenRouter:ApiKey", "   ", "Codexplorer:OpenRouter:ApiKey cannot be empty or whitespace.")]
    public async Task AddCodexplorerOptions_WhenRequiredTextValueIsWhitespace_FailsStartup(
        string key,
        string value,
        string expectedMessage)
    {
        var settings = CreateValidSettings();
        settings[key] = value;

        using var host = BuildHost(settings);
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        exception.Failures.Should().ContainSingle().Which.Should().Be(expectedMessage);
    }

    [Fact]
    public async Task AddCodexplorerOptions_WhenOpenRouterApiKeyIsMissing_FailsStartup()
    {
        var settings = CreateValidSettings();
        settings.Remove("Codexplorer:OpenRouter:ApiKey");
        using var host = BuildHost(settings);

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        exception.Failures.Should().ContainSingle().Which.Should().Be("Codexplorer:OpenRouter:ApiKey cannot be empty or whitespace.");
    }

    private static Dictionary<string, string?> CreateValidSettings()
    {
        return new Dictionary<string, string?>
        {
            ["Codexplorer:Budget:ContextWindowTokens"] = "16000",
            ["Codexplorer:Budget:SoftThresholdRatio"] = "0.7",
            ["Codexplorer:Budget:HardThresholdRatio"] = "0.9",
            ["Codexplorer:Budget:WindowSize"] = "5",
            ["Codexplorer:Model:Name"] = "google/gemini-2.5-flash",
            ["Codexplorer:Model:MaxOutputTokens"] = "2048",
            ["Codexplorer:Model:Temperature"] = "0.0",
            ["Codexplorer:Workspace:RootDirectory"] = "./workspace",
            ["Codexplorer:Workspace:CloneDepth"] = "1",
            ["Codexplorer:Workspace:MaxRepoSizeMB"] = "500",
            ["Codexplorer:Logging:SessionLogsDirectory"] = "./logs/sessions",
            ["Codexplorer:Logging:MinimumLevel"] = "Information",
            ["Codexplorer:OpenRouter:ApiKey"] = "test-api-key"
        };
    }

    private static IHost BuildHost(IReadOnlyDictionary<string, string?> settings)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddCodexplorerOptions(builder.Configuration);

        return builder.Build();
    }

    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class CodexplorerConfigurationCollection
    {
        public const string Name = "Codexplorer configuration";
    }
}
