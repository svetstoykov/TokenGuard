using System.Text.Json;
using Codexplorer.Automation.AgentComparison.Recording;
using FluentAssertions;

namespace Codexplorer.Automation.Tests.AgentComparison.Recording;

/// <summary>Verifies the comparison boundary and its observable outcomes.</summary>
public sealed class ComparisonCredentialResolverTests
{
    /// <summary>Verifies resolve prefers environment and inspect never contains credential.</summary>
    [Fact]
    public async Task Resolve_PrefersEnvironment_AndInspectNeverContainsCredential()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var settings = Path.Combine(directory, "appsettings.Development.json");
        await File.WriteAllTextAsync(settings, "{\"Codexplorer\":{\"OpenRouter\":{\"ApiKey\":\"file-secret-value\"}}}");
        var resolver = new ComparisonCredentialResolver(() => "environment-secret-value", settings);

        var resolved = resolver.Resolve();
        var presence = resolver.Inspect();

        resolved.ApiKey.Should().Be("environment-secret-value");
        resolved.Source.Should().Be("OPENROUTER_API_KEY");
        presence.Present.Should().BeTrue();
        presence.Source.Should().Be("OPENROUTER_API_KEY");
        JsonSerializer.Serialize(presence).Should().NotContain("secret-value");
        Directory.Delete(directory, recursive: true);
    }

    /// <summary>Verifies resolve falls back to ignored development settings.</summary>
    [Fact]
    public async Task Resolve_FallsBackToIgnoredDevelopmentSettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var settings = Path.Combine(directory, "appsettings.Development.json");
        await File.WriteAllTextAsync(settings, "{\"Codexplorer\":{\"OpenRouter\":{\"ApiKey\":\"fallback-secret\"}}}");
        var resolver = new ComparisonCredentialResolver(() => null, settings);

        var resolved = resolver.Resolve();

        resolved.ApiKey.Should().Be("fallback-secret");
        resolved.Source.Should().Be("appsettings.Development.json");
        Directory.Delete(directory, recursive: true);
    }
}
