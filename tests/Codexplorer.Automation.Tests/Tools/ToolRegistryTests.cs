extern alias sample;

using System.Text.Json;
using FluentAssertions;
using sample::Codexplorer.Tools;
using sample::Codexplorer.Workspace;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Models;

namespace Codexplorer.Automation.Tests.Tools;

/// <summary>Verifies how the registry treats a tool call whose arguments the tool's schema does not declare.</summary>
public sealed class ToolRegistryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    /// <summary>Verifies a grep call with <c>path</c> returns an error that names it, lists the parameters and points to <c>pathGlob</c>.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task ExecuteAsync_GrepCalledWithPath_ReturnsErrorPointingToPathGlob()
    {
        this.WriteFile("src/settings.txt", "timeout = 30\n");
        var arguments = JsonSerializer.SerializeToElement(new { pattern = "timeout", path = "src" });

        var result = await CreateRegistry().ExecuteAsync("grep", arguments, this.CreateContext(), CancellationToken.None);

        result.Should().Be(
            "Error: grep has no parameter 'path'. Declared parameters: pattern, pathGlob, maxMatches. Did you mean 'pathGlob'?");
    }

    /// <summary>Verifies an undeclared argument on another tool returns the same error and the tool does not run.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task ExecuteAsync_EditFileCalledWithUndeclaredArgument_ReturnsErrorAndLeavesFileUnchanged()
    {
        var path = this.WriteFile("settings.txt", "a = 1\n");
        var arguments = JsonSerializer.SerializeToElement(new { path = "settings.txt", oldText = "a = 1", newText = "a = 2", replaceAll = true });

        var result = await CreateRegistry().ExecuteAsync("edit_file", arguments, this.CreateContext(), CancellationToken.None);

        result.Should().Be("Error: edit_file has no parameter 'replaceAll'. Declared parameters: path, oldText, newText.");
        File.ReadAllText(path).Should().Be("a = 1\n");
    }

    /// <summary>Verifies an undeclared argument on an artifact tool returns the same error.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task ExecuteAsync_ArtifactToolCalledWithUndeclaredArgument_ReturnsError()
    {
        var arguments = JsonSerializer.SerializeToElement(new { path = "notes.md", content = "x", overwrite = true });

        var result = await CreateRegistry().ExecuteAsync("create_artifact", arguments, this.CreateContext(), CancellationToken.None);

        result.Should().Be("Error: create_artifact has no parameter 'overwrite'. Declared parameters: path, content.");
    }

    /// <summary>Verifies a call that uses only declared arguments runs the tool.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task ExecuteAsync_GrepCalledWithDeclaredArguments_ReturnsMatches()
    {
        this.WriteFile("src/settings.txt", "timeout = 30\n");
        this.WriteFile("docs/notes.txt", "timeout is 30\n");
        var arguments = JsonSerializer.SerializeToElement(new { pattern = "timeout", pathGlob = "src/**", maxMatches = 5 });

        var result = await CreateRegistry().ExecuteAsync("grep", arguments, this.CreateContext(), CancellationToken.None);

        result.Should().Be(string.Join(Environment.NewLine, "src/settings.txt:1: timeout = 30", "> 1| timeout = 30"));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(this._root))
        {
            Directory.Delete(this._root, recursive: true);
        }
    }

    private static ToolRegistry CreateRegistry()
    {
        return new ToolRegistry(new UnusedHttpClientFactory(), new UnusedTokenCounter(), new BraveSearchSettings(null));
    }

    private ToolContext CreateContext()
    {
        var clone = Path.Combine(this._root, "clone");
        Directory.CreateDirectory(clone);
        return new ToolContext(new Workspace("repo", "owner/repo", clone, DateTime.UnixEpoch, 0), Path.Combine(this._root, "artifacts"));
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
