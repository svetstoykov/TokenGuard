using Codexplorer.Automation.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace Codexplorer.Automation.Tests.Configuration;

/// <summary>Verifies immutable task snapshots and manifest provenance.</summary>
public sealed class AutomationTaskManifestLoaderTests
{
    /// <summary>Verifies later file changes preserve the original task snapshot.</summary>
    [Fact]
    public void LoadTasks_FileChangesAfterFirstLoad_UsesOriginalTasks()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, Manifest("original"));
            var loader = new AutomationTaskManifestLoader(
                Options.Create(new CodexplorerAutomationOptions { ManifestPath = path }),
                NullLogger<AutomationTaskManifestLoader>.Instance);
            loader.LoadTasks();
            File.WriteAllText(path, Manifest("changed"));

            var tasks = loader.LoadTasks();

            tasks.Single().TaskId.Should().Be("original");
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Verifies the manifest hash describes the bytes originally executed.</summary>
    [Fact]
    public void LoadSnapshot_FileChangesAfterFirstLoad_PreservesOriginalByteHash()
    {
        var path = Path.GetTempFileName();
        var original = Encoding.UTF8.GetBytes(Manifest("original"));
        try
        {
            File.WriteAllBytes(path, original);
            var loader = new AutomationTaskManifestLoader(
                Options.Create(new CodexplorerAutomationOptions { ManifestPath = path }),
                NullLogger<AutomationTaskManifestLoader>.Instance);
            loader.LoadSnapshot();
            File.WriteAllText(path, Manifest("changed"));

            var snapshot = loader.LoadSnapshot();

            snapshot.Sha256.Should().Be(Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant());
            snapshot.Tasks.Single().TaskId.Should().Be("original");
            snapshot.Provenance.Should().Be("file");
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Verifies invalid task definitions are rejected during snapshot loading.</summary>
    [Fact]
    public void LoadSnapshot_InvalidTask_RejectsTheLoadedManifest()
    {
        var loader = new AutomationTaskManifestLoader(Options.Create(new CodexplorerAutomationOptions
        {
            ManifestPath = null, Tasks = [new AutomationTaskDefinition { TaskId = "invalid" }]
        }), NullLogger<AutomationTaskManifestLoader>.Instance);

        var load = () => loader.LoadSnapshot();

        load.Should().Throw<OptionsValidationException>();
    }

    private static string Manifest(string taskId) => $$"""
        {"tasks":[{"taskId":"{{taskId}}","title":"Task","repositoryUrl":"https://github.com/example/repo",
        "initialPrompt":"Do not modify repository source files."}]}
        """;
}
