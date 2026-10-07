using FluentAssertions;

namespace Codexplorer.Automation.Tests;

/// <summary>Verifies offline command dispatch at the executable boundary.</summary>
public sealed class EntryPointTests
{
    /// <summary>Verifies comparison help succeeds before host configuration is required.</summary>
    [Fact]
    public async Task CompareHelpRunsWithoutConstructingTheHost()
    {
        var exitCode = await Program.Main(["compare", "--help"]);

        exitCode.Should().Be(0);
    }
}
