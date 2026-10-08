using Codexplorer.Automation.Configuration;
using FluentAssertions;

namespace Codexplorer.Automation.Tests.Configuration;

/// <summary>Verifies how the report output directory is resolved.</summary>
public sealed class OutputPathResolverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tg-output-path-" + Guid.NewGuid().ToString("N"));

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(this._root))
        {
            Directory.Delete(this._root, recursive: true);
        }
    }

    /// <summary>Verifies a relative path resolves against the nearest ancestor that contains a git directory.</summary>
    [Fact]
    public void Resolve_RelativePathInsideRepository_ResolvesAgainstRepositoryRoot()
    {
        var applicationDirectory = Path.Combine(this._root, "samples", "bin");
        Directory.CreateDirectory(applicationDirectory);
        Directory.CreateDirectory(Path.Combine(this._root, ".git"));

        var resolved = OutputPathResolver.Resolve(".artifacts/reports/benchmark", applicationDirectory);

        resolved.Should().Be(Path.Combine(this._root, ".artifacts", "reports", "benchmark"));
    }

    /// <summary>Verifies an absolute path is used as given.</summary>
    [Fact]
    public void Resolve_AbsolutePath_ReturnsPathUnchanged()
    {
        var applicationDirectory = Path.Combine(this._root, "samples", "bin");
        Directory.CreateDirectory(applicationDirectory);
        Directory.CreateDirectory(Path.Combine(this._root, ".git"));
        var absolute = Path.Combine(this._root, "elsewhere");

        var resolved = OutputPathResolver.Resolve(absolute, applicationDirectory);

        resolved.Should().Be(absolute);
    }
}
