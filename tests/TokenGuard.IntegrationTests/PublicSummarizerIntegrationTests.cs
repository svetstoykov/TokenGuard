using FluentAssertions;
using TokenGuard.Core;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Configuration;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models.Content;
using TokenGuard.Core.Options;
using TokenGuard.Core.Summarization;

namespace TokenGuard.IntegrationTests;

public sealed class PublicSummarizerIntegrationTests
{
    [Theory]
    [InlineData(SummarizerFinishReason.Completed)]
    [InlineData(SummarizerFinishReason.OutputLimit)]
    [InlineData(SummarizerFinishReason.Other)]
    public async Task PrepareAsync_WithACustomSummarizer_ReturnsItsSummary(SummarizerFinishReason finishReason)
    {
        // Arrange
        var summarizer = new CustomSummarizer("Earlier work is complete.", finishReason);
        using var context = CreateContext(summarizer);

        // Act
        var result = await context.PrepareAsync();

        // Assert
        result.Outcome.Should().Be(PrepareOutcome.Compacted);
        result.SummarizationError.Should().BeNull();
        result.Messages.Should().Contain(message => message.State == CompactionState.Summarized
            && message.Segments.Any(segment => segment.Content == "Earlier work is complete."));
        summarizer.Requests.Should().ContainSingle().Which.SystemPrompt.Should().Contain("Summarize release decisions only.");
        summarizer.Requests[0].UserPrompt.Should().Contain("Transcript:");
        summarizer.Requests[0].MaxOutputTokens.Should().Be(200);
    }

    [Theory]
    [InlineData("", SummarizerFinishReason.Completed, false)]
    [InlineData("Cannot summarize.", SummarizerFinishReason.Refused, false)]
    [InlineData("", SummarizerFinishReason.Other, true)]
    public async Task PrepareAsync_WhenACustomSummaryFails_RemembersTheAttemptUntilHistoryChanges(
        string text, SummarizerFinishReason finishReason, bool throws)
    {
        // Arrange
        var summarizer = new CustomSummarizer(text, finishReason, throws);
        using var context = CreateContext(summarizer);

        // Act
        var first = await context.PrepareAsync();
        var repeated = await context.PrepareAsync();

        // Assert
        first.SummarizationError.Should().BeOfType<InvalidOperationException>();
        repeated.SummarizationError.Should().BeNull();
        summarizer.Requests.Should().ContainSingle();
        repeated.Messages.Should().NotContain(message => message.State == CompactionState.Summarized);

        context.AddUserMessage("Continue the release.");
        var changed = await context.PrepareAsync();
        changed.SummarizationError.Should().BeOfType<InvalidOperationException>();
        summarizer.Requests.Should().HaveCount(2);
    }

    private static IConversationContext CreateContext(ILlmSummarizer summarizer)
    {
        var options = new LlmSummarizationOptions(windowSize: 1, minSummaryTokens: 5, maxSummaryTokens: 100)
        {
            SummaryInstructions = "Summarize release decisions only.",
        };
        var configuration = new ConversationConfigBuilder().WithMaxTokens(1_000).WithoutEmergencyThreshold()
            .UseLlmSummarization(summarizer, options).Build();
        var context = new ConversationContextFactory(configuration).Create();
        for (var turn = 0; turn < 4; turn++)
        {
            context.AddUserMessage(string.Concat(Enumerable.Repeat("Explain the release decisions. ", 100)));
            context.RecordModelResponse([new TextContent(string.Concat(Enumerable.Repeat("The release decisions are recorded. ", 100)))]);
        }

        return context;
    }

    /// <summary>
    /// Represents a consumer summarizer implemented entirely through the public contract.
    /// </summary>
    private sealed class CustomSummarizer(string text, SummarizerFinishReason finishReason, bool throws = false) : ILlmSummarizer
    {
        /// <summary>
        /// Gets the requests received at the model boundary.
        /// </summary>
        public List<SummarizerRequest> Requests { get; } = [];

        /// <inheritdoc />
        public Task<SummarizerResponse> SummarizeAsync(SummarizerRequest request, CancellationToken cancellationToken = default)
        {
            this.Requests.Add(request);
            if (throws)
                throw new InvalidOperationException("The provider failed.");

            return Task.FromResult(new SummarizerResponse(text, finishReason));
        }
    }
}
