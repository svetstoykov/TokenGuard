extern alias sample;

using System.Text.RegularExpressions;
using FluentAssertions;
using sample::Codexplorer.Tools;
using sample::Codexplorer.Workspace;

namespace Codexplorer.Automation.Tests.Tools;

/// <summary>Verifies how the grep tool bounds the length of a line and the size of one result.</summary>
public sealed class GrepToolTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    /// <summary>Verifies matches that fit both caps are returned whole, with their context lines and no marker.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task HandleAsync_MatchesFitBothCaps_ReturnsThemWhole()
    {
        var workspace = this.CreateWorkspace();
        this.WriteFile("src/settings.txt", "retries = 3\ntimeout = 30\nmode = fast\n");

        var result = await new GrepTool().HandleAsync(new("timeout", null, null), workspace, CancellationToken.None);

        result.Should().Be(string.Join(
            Environment.NewLine, "src/settings.txt:2: timeout = 30", "  1| retries = 3", "> 2| timeout = 30", "  3| mode = fast"));
    }

    /// <summary>Verifies a matched line longer than the line cap is cut at the cap and marked as cut.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task HandleAsync_MatchedLineLongerThanLineCap_CutsAndMarksIt()
    {
        var workspace = this.CreateWorkspace();
        var kept = "needle" + new string('a', GrepTool.LineCharacterCap - "needle".Length);
        this.WriteFile("bundle.min.js", kept + new string('b', 5000) + "\n");

        var result = await new GrepTool().HandleAsync(new("needle", null, null), workspace, CancellationToken.None);

        var cutLine = $"{kept} [... line cut at {GrepTool.LineCharacterCap} characters ...]";
        result.Should().Be(string.Join(Environment.NewLine, $"bundle.min.js:1: {cutLine}", $"> 1| {cutLine}"));
    }

    /// <summary>Verifies a context line longer than the line cap is cut and marked like a matched line.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task HandleAsync_ContextLineLongerThanLineCap_CutsAndMarksIt()
    {
        var workspace = this.CreateWorkspace();
        this.WriteFile("data.txt", new string('x', 5000) + "\nneedle\n" + new string('y', 5000) + "\n");

        var result = await new GrepTool().HandleAsync(new("needle", null, null), workspace, CancellationToken.None);

        var marker = $" [... line cut at {GrepTool.LineCharacterCap} characters ...]";
        result.Should().Be(string.Join(
            Environment.NewLine,
            "data.txt:2: needle",
            $"  1| {new string('x', GrepTool.LineCharacterCap)}{marker}",
            "> 2| needle",
            $"  3| {new string('y', GrepTool.LineCharacterCap)}{marker}"));
    }

    /// <summary>Verifies a search whose matches exceed the size cap returns a result within the cap.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task HandleAsync_MatchesExceedResultSizeCap_ReturnsResultWithinCap()
    {
        var workspace = this.CreateWorkspace();
        this.WriteFile("data.txt", LongMatchingLines(80));

        var result = await new GrepTool().HandleAsync(new("needle", null, null), workspace, CancellationToken.None);

        result.Length.Should().BeLessThanOrEqualTo(GrepTool.ResultCharacterCap);
    }

    /// <summary>Verifies the marker of a result cut by the size cap counts the omitted matches and advises a narrower search.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task HandleAsync_MatchesExceedResultSizeCap_MarkerReportsOmittedMatchesAndAdvice()
    {
        var workspace = this.CreateWorkspace();
        this.WriteFile("data.txt", LongMatchingLines(80));

        var result = await new GrepTool().HandleAsync(new("needle", null, null), workspace, CancellationToken.None);

        var shown = Regex.Matches(result, @"^data\.txt:\d+: ", RegexOptions.Multiline).Count;
        shown.Should().BeInRange(1, 79);
        result.Should().EndWith(
            $"[... truncated: {80 - shown} more matches; cap {GrepTool.ResultCharacterCap} characters hit; narrow the pattern or pathGlob ...]");
    }

    /// <summary>Verifies the description shown to the model states the line cap and the result size cap.</summary>
    [Fact]
    public void Schema_Description_StatesBothCaps()
    {
        var description = new GrepTool().Schema.Function.Description;

        description.Should().Contain($"{GrepTool.LineCharacterCap} characters").And.Contain($"{GrepTool.ResultCharacterCap} characters");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(this._root))
        {
            Directory.Delete(this._root, recursive: true);
        }
    }

    private static string LongMatchingLines(int count)
    {
        return string.Concat(Enumerable.Range(1, count).Select(static index => $"needle {index} {new string('z', 400)}\n"));
    }

    private Workspace CreateWorkspace()
    {
        var clone = Path.Combine(this._root, "clone");
        Directory.CreateDirectory(clone);
        return new Workspace("repo", "owner/repo", clone, DateTime.UnixEpoch, 0);
    }

    private void WriteFile(string relativePath, string content)
    {
        var path = Path.Combine(this._root, "clone", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
