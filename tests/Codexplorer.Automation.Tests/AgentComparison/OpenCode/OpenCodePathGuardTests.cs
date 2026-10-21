using Codexplorer.Automation.AgentComparison.OpenCode;
using FluentAssertions;

namespace Codexplorer.Automation.Tests.AgentComparison.OpenCode;

/// <summary>Verifies the comparison boundary and its observable outcomes.</summary>
public sealed class OpenCodePathGuardTests
{
    /// <summary>Verifies child file rejects traversal and accepts flat artifact names.</summary>
    [Fact]
    public void ChildFile_RejectsTraversalAndAcceptsFlatArtifactNames()
    {
        var root = Path.Combine(Path.GetTempPath(), $"opencode-path-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            OpenCodePathGuard.ChildFile(root, "session-events.jsonl")
                .Should().Be(Path.Combine(root, "session-events.jsonl"));
            var traversal = () => OpenCodePathGuard.ChildFile(root, "../outside.json");
            traversal.Should().Throw<UnauthorizedAccessException>();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Verifies prepare artifact root rejects symlinked root.</summary>
    [Fact]
    public void PrepareArtifactRoot_RejectsSymlinkedRoot()
    {
        if (OperatingSystem.IsWindows())
            return;

        var root = Path.Combine(Path.GetTempPath(), $"opencode-link-{Guid.NewGuid():N}");
        var actual = Path.Combine(root, "actual");
        var link = Path.Combine(root, "linked");
        Directory.CreateDirectory(actual);
        try
        {
            Directory.CreateSymbolicLink(link, actual);
            var action = () => OpenCodePathGuard.PrepareArtifactRoot(link);
            action.Should().Throw<UnauthorizedAccessException>();
            var stateAction = () => OpenCodePathGuard.PrepareIsolatedRoot(link, "runtime state");
            stateAction.Should().Throw<UnauthorizedAccessException>();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
