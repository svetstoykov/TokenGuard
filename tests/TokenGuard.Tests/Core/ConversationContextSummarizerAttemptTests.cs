using FluentAssertions;
using TokenGuard.Core;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Configuration;
using TokenGuard.Core.Models.Content;
using TokenGuard.Core.Options;
using TokenGuard.Tests.Diagnostics;

namespace TokenGuard.Tests.Core;

public sealed class ConversationContextSummarizerAttemptTests
{
    [Fact]
    public async Task PrepareAsync_WhenTheSummaryIsTooLargeAndNoMessageIsAdded_CallsTheSummarizerOnce()
    {
        // Arrange
        var summarizer = ScriptedSummarizer.Returning(Prose(6_000));
        using var context = CreateOverBudgetContext(summarizer);

        // Act
        await context.PrepareAsync();
        await context.PrepareAsync();
        await context.PrepareAsync();

        // Assert
        summarizer.Calls.Should().Be(1);
    }

    [Fact]
    public async Task PrepareAsync_WhenTheSummarizerThrowsAndNoMessageIsAdded_CallsTheSummarizerOnceAndReportsTheErrorOnce()
    {
        // Arrange
        var summarizer = ScriptedSummarizer.Throwing(new InvalidOperationException("empty answer"));
        using var context = CreateOverBudgetContext(summarizer);

        // Act
        var first = await context.PrepareAsync();
        var second = await context.PrepareAsync();
        var third = await context.PrepareAsync();

        // Assert
        summarizer.Calls.Should().Be(1);
        first.SummarizationError.Should().BeOfType<InvalidOperationException>();
        second.SummarizationError.Should().BeNull();
        third.SummarizationError.Should().BeNull();
        third.Messages.Should().Equal(first.Messages);
    }

    [Fact]
    public async Task PrepareAsync_WhenAMessageIsAddedAfterAFailedSummary_CallsTheSummarizerAgain()
    {
        // Arrange
        var summarizer = ScriptedSummarizer.Throwing(new InvalidOperationException("empty answer"));
        using var context = CreateOverBudgetContext(summarizer);
        await context.PrepareAsync();
        context.AddUserMessage(Prose(50));

        // Act
        await context.PrepareAsync();

        // Assert
        summarizer.Calls.Should().Be(2);
    }

    /// <summary>
    ///     Creates a context whose text-only history is over the limit, so masking cannot help and the summarizer is asked.
    /// </summary>
    /// <param name="summarizer">The summarizer the context uses.</param>
    /// <returns>The context, holding ten recorded messages.</returns>
    private static IConversationContext CreateOverBudgetContext(ILlmSummarizer summarizer)
    {
        var builder = new ConversationConfigBuilder().WithMaxTokens(1_500);
        builder.SetLlmSummarizer(() => summarizer, "Test", new LlmSummarizationOptions(windowSize: 2, minSummaryTokens: 100, maxSummaryTokens: 500));
        var context = new ConversationContextFactory(builder.Build()).Create();

        for (var turn = 0; turn < 5; turn++)
        {
            context.AddUserMessage(Prose(200));
            context.RecordModelResponse([new TextContent(Prose(200))]);
        }

        return context;
    }

    private static string Prose(int words) => string.Join(' ', Enumerable.Repeat("word", words));
}
