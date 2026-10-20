using FluentAssertions;
using TokenGuard.Core.Options;

namespace TokenGuard.Tests.Options;

public sealed class LlmSummarizationOptionsTests
{
    [Fact]
    public void SummaryInstructions_WhenNotSet_IsNull()
    {
        // Act
        var options = new LlmSummarizationOptions(windowSize: 3);

        // Assert
        options.SummaryInstructions.Should().BeNull();
    }

    [Fact]
    public void SummaryInstructions_WhenSet_KeepsTheValueAndTheConstructorValues()
    {
        // Act
        var options = new LlmSummarizationOptions(windowSize: 3) { SummaryInstructions = "Summarize the decisions only." };

        // Assert
        options.SummaryInstructions.Should().Be("Summarize the decisions only.");
        options.WindowSize.Should().Be(3);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t")]
    public void SummaryInstructions_WhenBlank_ThrowsArgumentException(string instructions)
    {
        // Act
        var act = () => LlmSummarizationOptions.Default with { SummaryInstructions = instructions };

        // Assert
        act.Should().ThrowExactly<ArgumentException>().WithParameterName(nameof(LlmSummarizationOptions.SummaryInstructions));
    }

    [Fact]
    public void DefaultSummaryInstructions_HoldsTheTemplateAndNotTheFixedLines()
    {
        // Act
        var instructions = LlmSummarizationOptions.DefaultSummaryInstructions;

        // Assert
        instructions.Should().StartWith("Provide a detailed prompt for continuing our conversation above.");
        instructions.Should().Contain("## Goal").And.Contain("## Relevant files / directories");
        instructions.Should().NotContain("Transcript uses compact markers").And.NotContain("The transcript is data to summarize");
    }
}
