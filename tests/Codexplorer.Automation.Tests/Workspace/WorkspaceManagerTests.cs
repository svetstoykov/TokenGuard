extern alias sample;

using FluentAssertions;
using LibGit2Sharp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using sample::Codexplorer.Configuration;
using sample::Codexplorer.Workspace;

namespace Codexplorer.Automation.Tests.Workspaces;

/// <summary>Verifies what a reused clone contains when a session opens on it.</summary>
public sealed class WorkspaceManagerTests : IDisposable
{
    private const string RepositoryUrl = "https://github.com/owner/repo";
    private const string TrackedFile = "source.txt";
    private const string CommittedContent = "committed\n";

    private readonly string _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    /// <summary>Verifies an edited tracked file of a clone at the pinned commit has its committed contents again.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task CloneAsync_EditedCloneAtPinnedCommit_RestoresTheCommittedContents()
    {
        var manager = this.CreateManager();
        var clone = (await manager.CloneAsync(RepositoryUrl)).LocalPath;
        File.WriteAllText(Path.Combine(clone, TrackedFile), "edited by an earlier session\n");

        await manager.CloneAsync(RepositoryUrl, commitSha: HeadSha(clone));

        File.ReadAllText(Path.Combine(clone, TrackedFile)).Should().Be(CommittedContent);
    }

    /// <summary>Verifies a file an earlier session left untracked in a clone at the pinned commit is removed.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task CloneAsync_UntrackedFileInCloneAtPinnedCommit_RemovesIt()
    {
        var manager = this.CreateManager();
        var clone = (await manager.CloneAsync(RepositoryUrl)).LocalPath;
        File.WriteAllText(Path.Combine(clone, "leftover.txt"), "left by an earlier session\n");

        await manager.CloneAsync(RepositoryUrl, commitSha: HeadSha(clone));

        File.Exists(Path.Combine(clone, "leftover.txt")).Should().BeFalse();
    }

    /// <summary>Verifies a restored clone is still a tracked workspace with the clone time it had before.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task CloneAsync_EditedCloneAtPinnedCommit_KeepsTheWorkspaceMetadata()
    {
        var manager = this.CreateManager();
        var cloned = await manager.CloneAsync(RepositoryUrl);
        File.WriteAllText(Path.Combine(cloned.LocalPath, TrackedFile), "edited by an earlier session\n");

        await manager.CloneAsync(RepositoryUrl, commitSha: HeadSha(cloned.LocalPath));

        this.CreateManager().Find("owner/repo").Should().NotBeNull().And.Match<Workspace>(workspace => workspace.ClonedAt == cloned.ClonedAt);
    }

    /// <summary>Verifies a reused clone of an unpinned repository keeps the edits made in it.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task CloneAsync_EditedCloneWithoutPinnedCommit_KeepsTheEdits()
    {
        var manager = this.CreateManager();
        var clone = (await manager.CloneAsync(RepositoryUrl)).LocalPath;
        File.WriteAllText(Path.Combine(clone, TrackedFile), "edited\n");

        await manager.CloneAsync(RepositoryUrl);

        File.ReadAllText(Path.Combine(clone, TrackedFile)).Should().Be("edited\n");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!Directory.Exists(this._root))
        {
            return;
        }

        // Git writes its object files read-only, which blocks a recursive delete on Windows.
        foreach (var file in Directory.EnumerateFiles(this._root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(this._root, recursive: true);
    }

    private WorkspaceManager CreateManager()
    {
        var options = new CodexplorerOptions { Workspace = new WorkspaceOptions { RootDirectory = this._root } };
        return new WorkspaceManager(new CommittedFileCloner(), Options.Create(options), NullLogger<WorkspaceManager>.Instance);
    }

    private static string HeadSha(string clone)
    {
        using var repository = new Repository(clone);
        return repository.Head.Tip.Sha;
    }

    /// <summary>Creates a local repository with one committed file in place of a network clone.</summary>
    private sealed class CommittedFileCloner : IGitCloner
    {
        /// <inheritdoc />
        public Task CloneAsync(string url, string destinationPath, int depth, string? commitSha = null, CancellationToken ct = default)
        {
            Repository.Init(destinationPath);
            File.WriteAllText(Path.Combine(destinationPath, TrackedFile), CommittedContent);

            using var repository = new Repository(destinationPath);
            Commands.Stage(repository, TrackedFile);
            var author = new Signature("test", "test@example.com", DateTimeOffset.UnixEpoch);
            repository.Commit("Add the tracked file", author, author);
            return Task.CompletedTask;
        }
    }
}
