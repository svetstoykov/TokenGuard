using Codexplorer.Automation.AgentComparison;
using FluentAssertions;

namespace Codexplorer.Automation.Tests.AgentComparison.Preparation;

/// <summary>Verifies the comparison boundary and its observable outcomes.</summary>
public sealed class PromptAdaptationTests
{
    /// <summary>Verifies adapt prompt changes only artifact destination and native tool terms.</summary>
    [Fact]
    public void AdaptPrompt_ChangesOnlyArtifactDestinationAndNativeToolTerms()
    {
        const string source = "Write evidence.md with the edit tool, then use the artifact tools. Keep `key: 42` unchanged.";

        var adapted = LaunchPreparation.AdaptPrompt(source);

        adapted.Should().Be(
            "Write ${ARTIFACTS_DIR}/evidence.md with OpenCode's edit tool, then use OpenCode's file tools. "
            + "Keep `key: 42` unchanged.");
    }

    /// <summary>Verifies adapt prompt preserves prompts without tool or artifact terminology.</summary>
    [Fact]
    public void AdaptPrompt_PreservesPromptsWithoutToolOrArtifactTerminology()
    {
        const string source = "Read src/example.cs and report threshold 17.";

        LaunchPreparation.AdaptPrompt(source).Should().Be(source);
    }
}
