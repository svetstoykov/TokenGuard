extern alias sample;

using FluentAssertions;
using Microsoft.Extensions.Options;
using sample::Codexplorer.Configuration;
using sample::Codexplorer.Sessions;
using sample::Codexplorer.Workspace;

namespace Codexplorer.Automation.Tests.Sessions;

/// <summary>Verifies where a session directory may be created relative to the cloned repository.</summary>
public sealed class SessionDirectoryFactoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    /// <summary>Verifies a requested path at or below the clone root is rejected before anything is created there.</summary>
    /// <param name="relativePath">The requested session directory, relative to the clone root.</param>
    [Theory]
    [InlineData("")]
    [InlineData("notes")]
    [InlineData("deep/notes")]
    public void Create_RequestedPathInsideClone_ThrowsAndCreatesNothing(string relativePath)
    {
        var workspace = this.CreateWorkspace();
        var factory = new SessionDirectoryFactory(Options.Create(new CodexplorerOptions()));

        var create = () => factory.Create(workspace, Path.Combine(workspace.LocalPath, relativePath));

        create.Should().Throw<InvalidOperationException>();
        Directory.EnumerateFileSystemEntries(workspace.LocalPath).Should().BeEmpty();
    }

    /// <summary>Verifies a requested path beside the clone, sharing its name prefix, is created with an artifacts folder.</summary>
    [Fact]
    public void Create_RequestedPathBesideClone_CreatesArtifactsFolder()
    {
        var workspace = this.CreateWorkspace();
        var factory = new SessionDirectoryFactory(Options.Create(new CodexplorerOptions()));

        var directory = factory.Create(workspace, workspace.LocalPath + "-session");

        Directory.Exists(Path.Combine(workspace.LocalPath + "-session", "artifacts")).Should().BeTrue();
        directory.ArtifactsPath.Should().Be(Path.Combine(workspace.LocalPath + "-session", "artifacts"));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(this._root))
        {
            Directory.Delete(this._root, recursive: true);
        }
    }

    private Workspace CreateWorkspace()
    {
        var clone = Path.Combine(this._root, "clone");
        Directory.CreateDirectory(clone);
        return new Workspace("repo", "owner/repo", clone, DateTime.UtcNow, 0);
    }
}
