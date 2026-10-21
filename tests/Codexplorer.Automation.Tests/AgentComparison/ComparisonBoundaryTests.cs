extern alias sample;

using Codexplorer.Automation.AgentComparison;
using FluentAssertions;
using Factory = sample::Codexplorer.Configuration.OpenRouterChatClientFactory;

namespace Codexplorer.Automation.Tests.AgentComparison;

/// <summary>Verifies fail-closed launch and filesystem boundaries.</summary>
public sealed class ComparisonBoundaryTests
{
    /// <summary>Rejects a live command before resolving any provider configuration.</summary>
    [Fact]
    public async Task Run_WithoutExecutionFlag_RefusesBeforePlanOrCredentials()
    {
        using var output = new StringWriter();
        var code = await AgentComparisonCommand.RunAsync(["run", "--plan", "nonexistent.json"], output);
        code.Should().Be(1);
        output.ToString().Should().Contain("--execute-live");
    }

    /// <summary>Resolves the ignored source configuration when no sample output directory exists.</summary>
    [Fact]
    public void CredentialLocation_UnbuiltSample_UsesOriginalIgnoredProjectSettings()
    {
        var root = Path.Combine(Path.GetTempPath(), "credential-location-" + Guid.NewGuid().ToString("N"));
        var sampleDirectory = Path.Combine(root, "samples", "Codexplorer", "src");
        Directory.CreateDirectory(sampleDirectory);
        try
        {
            File.WriteAllText(Path.Combine(sampleDirectory, "appsettings.Development.json"), "{}");
            var marker = AgentComparisonCommand.FindCredentialExecutable(root);
            Path.GetDirectoryName(marker).Should().Be(sampleDirectory);
            Directory.Exists(Path.Combine(sampleDirectory, "bin")).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Rejects endpoints capable of routing model traffic publicly.</summary>
    /// <param name="url">The invalid override.</param>
    [Theory]
    [InlineData("https://127.0.0.1/v1")]
    [InlineData("https://openrouter.ai/api/v1")]
    [InlineData("http://provider.invalid/v1")]
    [InlineData("http://user:secret@localhost/v1")]
    [InlineData("http://127.0.0.1/v1?redirect=public")]
    [InlineData("")]
    public void Endpoint_PublicOrAmbiguousOverride_IsRejected(string url)
    {
        var action = () => Factory.ResolveEndpoint(url);
        action.Should().Throw<InvalidOperationException>();
    }

    /// <summary>Accepts an explicit HTTP loopback endpoint while preserving the default when absent.</summary>
    [Fact]
    public void Endpoint_LoopbackOverride_IsUsed()
    {
        Factory.ResolveEndpoint("http://127.0.0.1:4567/v1").AbsoluteUri.Should().Be("http://127.0.0.1:4567/v1");
        Factory.ResolveEndpoint(null).AbsoluteUri.Should().Be("https://openrouter.ai/api/v1");
    }

    /// <summary>Rejects a repository link that could bypass a native tool's lexical path checks.</summary>
    [Fact]
    public void Repository_ExternalSymlink_IsRejectedBeforeRuntimeLaunch()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(root, "outside"), Path.GetTempPath());
            var action = () => ComparisonRepositorySafety.Validate(root);
            action.Should().Throw<InvalidDataException>().WithMessage("*escapes*");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Rejects traversal and links before reading evidence files.</summary>
    [Fact]
    public void Artifacts_TraversalAndSymlink_AreRejected()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var traversal = () => ComparisonArtifacts.Resolve(root, "../outside");
            traversal.Should().Throw<InvalidDataException>();
            File.CreateSymbolicLink(Path.Combine(root, "escape"), Path.Combine(root, "missing"));
            var linked = () => ComparisonArtifacts.Resolve(root, "escape");
            linked.Should().Throw<InvalidDataException>();
            var inventory = () => ComparisonArtifacts.Inventory(root);
            inventory.Should().Throw<InvalidDataException>();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
