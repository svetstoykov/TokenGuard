extern alias sample;

using System.Text;
using System.Text.Json;
using FluentAssertions;
using sample::Codexplorer.Tools;
using sample::Codexplorer.Workspace;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Models;

namespace Codexplorer.Automation.Tests.Tools;

/// <summary>Verifies how the edit tool changes, and refuses to change, files of a cloned repository.</summary>
public sealed class EditFileToolTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    /// <summary>Verifies a text that occurs once is replaced and the rest of the file is kept.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task HandleAsync_TextOccursOnce_ReplacesIt()
    {
        var workspace = this.CreateWorkspace();
        var path = this.WriteFile("src/settings.txt", "retries = 3\ntimeout = 30\nmode = fast\n");

        await new EditFileTool().HandleAsync(new("src/settings.txt", "timeout = 30", "timeout = 45"), workspace, CancellationToken.None);

        File.ReadAllText(path).Should().Be("retries = 3\ntimeout = 45\nmode = fast\n");
    }

    /// <summary>Verifies the result names the path and the old and new line ranges and leaves the file contents out.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task HandleAsync_TextOccursOnce_ReportsPathAndLineRanges()
    {
        var workspace = this.CreateWorkspace();
        this.WriteFile("src/settings.txt", "retries = 3\ntimeout = 30\nmode = fast\nlast = 1\n");

        var result = await new EditFileTool().HandleAsync(
            new("src/settings.txt", "timeout = 30\nmode = fast", "timeout = 45\nmode = slow\nburst = 2"), workspace, CancellationToken.None);

        result.Should().Be("Edited src/settings.txt: replaced lines 2-3; the new text is at lines 2-4.");
    }

    /// <summary>Verifies an absent text is reported as a failed edit and the file keeps its contents.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task HandleAsync_TextAbsent_ReturnsErrorAndLeavesFileUnchanged()
    {
        var workspace = this.CreateWorkspace();
        var path = this.WriteFile("settings.txt", "retries = 3\n");

        var result = await new EditFileTool().HandleAsync(new("settings.txt", "timeout = 30", "timeout = 45"), workspace, CancellationToken.None);

        result.Should().StartWith("Error: oldText not found in settings.txt");
        EditFileTool.IsSuccess(result).Should().BeFalse();
        File.ReadAllText(path).Should().Be("retries = 3\n");
    }

    /// <summary>Verifies a text that occurs more than once is refused with its count and the file keeps its contents.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task HandleAsync_TextRepeated_ReturnsErrorAndLeavesFileUnchanged()
    {
        var workspace = this.CreateWorkspace();
        var path = this.WriteFile("settings.txt", "limit = 5\nlimit = 5\n");

        var result = await new EditFileTool().HandleAsync(new("settings.txt", "limit = 5", "limit = 9"), workspace, CancellationToken.None);

        result.Should().StartWith("Error: oldText occurs 2 times in settings.txt");
        EditFileTool.IsSuccess(result).Should().BeFalse();
        File.ReadAllText(path).Should().Be("limit = 5\nlimit = 5\n");
    }

    /// <summary>Verifies a path that leaves the clone is rejected and the file outside keeps its contents.</summary>
    /// <param name="useAbsolutePath">Whether the outside file is named by its absolute path instead of a parent traversal.</param>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HandleAsync_PathOutsideClone_ThrowsAndLeavesFileUnchanged(bool useAbsolutePath)
    {
        var workspace = this.CreateWorkspace();
        var outside = Path.Combine(this._root, "outside.txt");
        File.WriteAllText(outside, "secret = 1\n");

        var edit = () => new EditFileTool().HandleAsync(
            new(useAbsolutePath ? outside : "../outside.txt", "secret = 1", "secret = 2"), workspace, CancellationToken.None);

        await edit.Should().ThrowAsync<PathEscapeException>();
        File.ReadAllText(outside).Should().Be("secret = 1\n");
    }

    /// <summary>Verifies a missing file is reported and is not created.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task HandleAsync_FileMissing_ReturnsErrorAndCreatesNothing()
    {
        var workspace = this.CreateWorkspace();

        var result = await new EditFileTool().HandleAsync(new("new.txt", "a", "b"), workspace, CancellationToken.None);

        result.Should().StartWith("Error: file not found: new.txt");
        File.Exists(Path.Combine(workspace.LocalPath, "new.txt")).Should().BeFalse();
    }

    /// <summary>Verifies a file under the clone's <c>.git</c> directory is refused and keeps its contents.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task HandleAsync_PathInsideGitDirectory_ReturnsErrorAndLeavesFileUnchanged()
    {
        var workspace = this.CreateWorkspace();
        var path = this.WriteFile(".git/HEAD", "ref: refs/heads/main\n");

        var result = await new EditFileTool().HandleAsync(new(".git/HEAD", "main", "other"), workspace, CancellationToken.None);

        result.Should().StartWith("Error: path is inside the .git directory");
        File.ReadAllText(path).Should().Be("ref: refs/heads/main\n");
    }

    /// <summary>Verifies text copied with line feeds matches a file that uses carriage-return line endings, which are kept.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task HandleAsync_FileUsesCrlf_MatchesLfTextAndKeepsCrlf()
    {
        var workspace = this.CreateWorkspace();
        var path = this.WriteFile("settings.txt", "a = 1\r\nb = 2\r\nc = 3\r\n");

        await new EditFileTool().HandleAsync(new("settings.txt", "a = 1\nb = 2", "a = 1\nb = 5"), workspace, CancellationToken.None);

        File.ReadAllText(path).Should().Be("a = 1\r\nb = 5\r\nc = 3\r\n");
    }

    /// <summary>Verifies a file that starts with a UTF-8 byte order mark still starts with it after an edit.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task HandleAsync_FileStartsWithByteOrderMark_KeepsIt()
    {
        var workspace = this.CreateWorkspace();
        var path = Path.Combine(workspace.LocalPath, "settings.txt");
        File.WriteAllText(path, "a = 1\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        await new EditFileTool().HandleAsync(new("settings.txt", "a = 1", "a = 2"), workspace, CancellationToken.None);

        File.ReadAllBytes(path).Should().Equal([0xEF, 0xBB, 0xBF, .. "a = 2\n"u8]);
    }

    /// <summary>Verifies the registry runs the tool under its published name with the argument names of its schema.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task ExecuteAsync_EditFileArguments_EditsTheFile()
    {
        var workspace = this.CreateWorkspace();
        var path = this.WriteFile("settings.txt", "a = 1\n");
        var registry = new ToolRegistry(new UnusedHttpClientFactory(), new UnusedTokenCounter(), new BraveSearchSettings(null));
        var arguments = JsonSerializer.SerializeToElement(new { path = "settings.txt", oldText = "a = 1", newText = "a = 2" });

        var context = new ToolContext(workspace, Path.Combine(this._root, "artifacts"));

        await registry.ExecuteAsync("edit_file", arguments, context, CancellationToken.None);

        File.ReadAllText(path).Should().Be("a = 2\n");
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
        return new Workspace("repo", "owner/repo", clone, DateTime.UnixEpoch, 0);
    }

    private string WriteFile(string relativePath, string content)
    {
        var path = Path.Combine(this._root, "clone", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private sealed class UnusedHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new NotSupportedException();
    }

    private sealed class UnusedTokenCounter : ITokenCounter
    {
        public int Count(ContextMessage contextMessage) => throw new NotSupportedException();

        public int Count(IEnumerable<ContextMessage> messages) => throw new NotSupportedException();
    }
}
