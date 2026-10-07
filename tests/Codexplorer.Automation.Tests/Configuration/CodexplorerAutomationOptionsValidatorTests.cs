using Codexplorer.Automation.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace Codexplorer.Automation.Tests.Configuration;

/// <summary>Verifies startup validation and effective helper credentials.</summary>
public sealed class CodexplorerAutomationOptionsValidatorTests
{
    /// <summary>Verifies startup validation leaves manifest loading to the snapshot loader.</summary>
    [Fact]
    public void Validate_MissingManifestFile_DoesNotReadManifest()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OPENROUTER_API_KEY"] = "test-environment-value"
        }).Build();
        var validator = new CodexplorerAutomationOptionsValidator(configuration);
        var options = new CodexplorerAutomationOptions
        {
            CodexplorerExecutablePath = typeof(CodexplorerAutomationOptionsValidator).Assembly.Location,
            ManifestPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json")
        };

        var result = validator.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    /// <summary>Verifies nonempty environment credentials take priority over configured credentials.</summary>
    /// <param name="environmentValue">The optional environment credential.</param>
    /// <param name="expected">The credential expected after precedence is applied.</param>
    [Theory]
    [InlineData("test-environment-value", "test-environment-value")]
    [InlineData("", "test-configured-value")]
    [InlineData(null, "test-configured-value")]
    public void ResolveCredential_UsesNonemptyEnvironmentBeforeConfiguredValue(string? environmentValue, string expected)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OPENROUTER_API_KEY"] = environmentValue
        }).Build();

        var result = HelperAiCredentials.Resolve(configuration, new AutomationHelperAiOptions { ApiKey = "test-configured-value" });

        result.Should().Be(expected);
    }
}
