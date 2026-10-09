using Codexplorer.Automation.Configuration;
using Codexplorer.Automation.Runner;
using Codexplorer.Automation.Scoring;
using FluentAssertions;

namespace Codexplorer.Automation.Tests.Runner;

/// <summary>Verifies canary isolation at the actual outgoing message boundary.</summary>
public sealed class HelperPromptTests
{
    /// <summary>Verifies codes in replies, questions, metadata and the fixed system wording are removed.</summary>
    /// <param name="canary">A legal code.</param>
    [Theory]
    [InlineData("REF-7Q4X-M2")]
    [InlineData("ANSWER")]
    public void CreateMessages_RemovesCodeFromBothCompletePrompts(string canary)
    {
        var split = canary[..3] + "**`" + canary[3..];
        var request = new RunnerHelperAiRequest("prefix" + canary + "suffix", RunnerTaskSize.Small, "/" + canary.ToLowerInvariant(),
            "original " + split, "question " + canary, "reply " + split, 1, 3, 1, false, canary);
        var messages = OpenRouterRunnerHelperAi.CreateMessages(request);
        messages.Should().HaveCount(2);
        foreach (var message in messages)
        {
            var text = string.Concat(message.Content.Select(part => part.Text));
            TextNormalizer.Normalize(text).Should().NotContainEquivalentOf(canary);
            if (message == messages[1] || canary == "ANSWER")
                text.Should().Contain("[...]");
        }
        request.AssistantText.Should().Contain(split);
    }

    /// <summary>Verifies ordinary helper prompts preserve Markdown and multiline formatting.</summary>
    [Fact]
    public void CreateMessages_NoProbe_PreservesFormatting()
    {
        var request = new RunnerHelperAiRequest(
            "task", RunnerTaskSize.Small, "/workspace", "**original**\nline", "question", "`reply`", 1, 3, 1, false);
        var text = string.Concat(OpenRouterRunnerHelperAi.CreateMessages(request)[1].Content.Select(part => part.Text));
        text.Should().Contain("**original**\nline").And.Contain("`reply`").And.Contain("Initial task prompt:\n");
    }
}
