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

    /// <summary>Verifies a task identifier that cannot name one session directory in the run folder is rejected.</summary>
    /// <param name="taskId">The task identifier under test.</param>
    [Theory]
    [InlineData("../shared")]
    [InlineData("group/task")]
    [InlineData("group\\task")]
    [InlineData("..")]
    [InlineData(".hidden")]
    [InlineData("task one")]
    [InlineData("run-report.json")]
    [InlineData("Run-Report.json")]
    public void LoadSnapshot_TaskIdIsNotOneDirectoryName_RejectsTheLoadedManifest(string taskId)
    {
        var loader = new AutomationTaskManifestLoader(
            Options.Create(new CodexplorerAutomationOptions { ManifestPath = null, Tasks = [ValidTask(taskId)] }),
            NullLogger<AutomationTaskManifestLoader>.Instance);

        var load = () => loader.LoadSnapshot();

        load.Should().Throw<OptionsValidationException>().Which.Failures.Should().ContainSingle().Which.Should().Contain("TaskId");
    }

    /// <summary>Verifies a task identifier made of letters, digits, dots, hyphens, and underscores is accepted.</summary>
    [Fact]
    public void LoadSnapshot_TaskIdIsOneDirectoryName_LoadsTheTask()
    {
        var loader = new AutomationTaskManifestLoader(
            Options.Create(new CodexplorerAutomationOptions { ManifestPath = null, Tasks = [ValidTask("batch-small_01.v2")] }),
            NullLogger<AutomationTaskManifestLoader>.Instance);

        var snapshot = loader.LoadSnapshot();

        snapshot.Tasks.Single().TaskId.Should().Be("batch-small_01.v2");
    }

    /// <summary>Verifies a repository commit that is not a full hexadecimal SHA is rejected with a message naming the task.</summary>
    /// <param name="repositoryCommit">The repository commit under test.</param>
    [Theory]
    [InlineData("")]
    [InlineData("724ae6d")]
    [InlineData("main")]
    [InlineData("724ae6d9a6e84ba4ad7eb3734dfe64250d4bf2fag")]
    [InlineData("z24ae6d9a6e84ba4ad7eb3734dfe64250d4bf2fa")]
    [InlineData(" 724ae6d9a6e84ba4ad7eb3734dfe64250d4bf2f")]
    public void LoadSnapshot_RepositoryCommitIsNotFullSha_RejectsTheLoadedManifestNamingTheTask(string repositoryCommit)
    {
        var loader = new AutomationTaskManifestLoader(
            Options.Create(new CodexplorerAutomationOptions
            {
                ManifestPath = null, Tasks = [ValidTask("pinned-task") with { RepositoryCommit = repositoryCommit }]
            }),
            NullLogger<AutomationTaskManifestLoader>.Instance);

        var load = () => loader.LoadSnapshot();

        load.Should().Throw<OptionsValidationException>().Which.Failures.Should().ContainSingle()
            .Which.Should().Contain("RepositoryCommit").And.Contain("'pinned-task'");
    }

    /// <summary>Verifies a full hexadecimal repository commit in either letter case is accepted.</summary>
    /// <param name="repositoryCommit">The repository commit under test.</param>
    [Theory]
    [InlineData("724ae6d9a6e84ba4ad7eb3734dfe64250d4bf2fa")]
    [InlineData("724AE6D9A6E84BA4AD7EB3734DFE64250D4BF2FA")]
    public void LoadSnapshot_RepositoryCommitIsFullSha_LoadsTheTask(string repositoryCommit)
    {
        var loader = new AutomationTaskManifestLoader(
            Options.Create(new CodexplorerAutomationOptions
            {
                ManifestPath = null, Tasks = [ValidTask("pinned-task") with { RepositoryCommit = repositoryCommit }]
            }),
            NullLogger<AutomationTaskManifestLoader>.Instance);

        var snapshot = loader.LoadSnapshot();

        snapshot.Tasks.Single().RepositoryCommit.Should().Be(repositoryCommit);
    }

    private static AutomationTaskDefinition ValidTask(string taskId) => new()
    {
        TaskId = taskId,
        Title = "Task",
        RepositoryUrl = "https://github.com/example/repo",
        InitialPrompt = "Do not modify repository source files."
    };

    private static string Manifest(string taskId) => $$"""
        {"tasks":[{"taskId":"{{taskId}}","title":"Task","repositoryUrl":"https://github.com/example/repo",
        "initialPrompt":"Do not modify repository source files."}]}
        """;
}
