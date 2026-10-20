using FluentAssertions;
using TokenGuard.Core;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;
using TokenGuard.Core.Options;
using TokenGuard.Core.Strategies;

namespace TokenGuard.IntegrationTests;

public sealed class PrepareReportingIntegrationTests
{
    [Theory]
    [InlineData(47)]
    [InlineData(19)]
    public async Task PrepareAsync_AfterAppend_ReportsEarlierPayloadAndCurrentProviderScale(int providerTokens)
    {
        // Arrange
        var counter = new LengthCounter();
        using var context = new ConversationContext(new ContextBudget(1_000), counter, new SlidingWindowStrategy(counter));
        context.SetSystemPrompt(new string('p', 10));
        context.AddUserMessage(new string('u', 20));
        var first = await context.PrepareAsync();
        context.RecordModelResponse([new TextContent("new")], providerTokens);

        // Act
        var next = await context.PrepareAsync();

        // Assert
        first.UnchangedPrefixMessages.Should().Be(0);
        first.UnchangedPrefixTokens.Should().Be(0);
        next.UnchangedPrefixMessages.Should().Be(2);
        next.UnchangedPrefixTokens.Should().Be(providerTokens);
        next.TokensAfterCompaction.Should().Be(providerTokens + 3);
        Total(next.TokenBreakdown).Should().Be(next.TokensAfterCompaction);
        next.TokenBreakdown.Pinned.Should().BeInRange(1, providerTokens);
        next.TokenBreakdown.Summary.Should().Be(0);
        next.SummarizationStatus.Should().Be(SummarizationStatus.NotNeeded);
    }

    [Fact]
    public async Task PrepareAsync_WhenMaskingBoundaryMoves_StopsPrefixAtChangedToolResult()
    {
        // Arrange
        var counter = new LengthCounter();
        var strategy = new TieredCompactionStrategy(counter, new SlidingWindowOptions(1, 0.5));
        using var context = new ConversationContext(new ContextBudget(1_000), counter, strategy);
        context.AddUserMessage("start");
        context.RecordModelResponse([new ToolUseContent("call", "read", "{}")]);
        context.RecordToolResult("call", "read", new string('r', 300));
        context.RecordModelResponse([new TextContent(new string('m', 300))]);
        context.AddUserMessage(new string('u', 100));
        await context.PrepareAsync();
        context.AddUserMessage(new string('n', 200));

        // Act
        var result = await context.PrepareAsync();

        // Assert
        result.ToolResultsMasked.Should().Be(1);
        result.MessagesSummarized.Should().Be(0);
        result.UnchangedPrefixMessages.Should().Be(2);
        result.UnchangedPrefixTokens.Should().Be(7);
        result.TokenBreakdown.Masked.Should().BeGreaterThan(0);
        result.TokenBreakdown.ToolResults.Should().Be(0);
        Total(result.TokenBreakdown).Should().Be(result.TokensAfterCompaction);
    }

    [Fact]
    public async Task PrepareAsync_WhenCheckpointReused_ReportsSummaryAndEveryEarlierMessageUnchanged()
    {
        // Arrange
        using var context = CreateSummaryContext(new SummaryModel("summary"));
        var first = await context.PrepareAsync();
        context.AddUserMessage("next");

        // Act
        var repeated = await context.PrepareAsync();

        // Assert
        first.SummarizationStatus.Should().Be(SummarizationStatus.NewSummary);
        first.MessagesSummarized.Should().Be(4);
        repeated.SummarizationStatus.Should().Be(SummarizationStatus.CheckpointReused);
        repeated.MessagesSummarized.Should().Be(4);
        repeated.ToolResultsMasked.Should().Be(0);
        repeated.UnchangedPrefixMessages.Should().Be(first.Messages.Count);
        repeated.UnchangedPrefixTokens.Should().Be(first.TokensAfterCompaction);
        repeated.TokenBreakdown.Summary.Should().Be(7);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrepareAsync_WhenSummaryThrowsOrOvershoots_ReportsFailureThenSkippedAttempt(bool throws)
    {
        // Arrange
        using var context = CreateSummaryContext(new SummaryModel(new string('s', 2_000), throws));

        // Act
        var first = await context.PrepareAsync();
        var repeated = await context.PrepareAsync();

        // Assert
        first.SummarizationStatus.Should().Be(SummarizationStatus.Failed);
        repeated.SummarizationStatus.Should().Be(SummarizationStatus.SkippedPreviousFailure);
        first.MessagesSummarized.Should().Be(0);
        first.TokenBreakdown.Summary.Should().Be(0);
        repeated.UnchangedPrefixMessages.Should().Be(first.Messages.Count);
        repeated.SummarizationError.Should().BeNull();
    }

    [Fact]
    public async Task PrepareAsync_WhenEmergencyDropsMaskedMessages_SeparatesSelectedStageCountsFromFinalBreakdown()
    {
        // Arrange
        var counter = new LengthCounter();
        var strategy = new TieredCompactionStrategy(counter, new SlidingWindowOptions(1, 0.5));
        using var context = new ConversationContext(new ContextBudget(1_000, emergencyThreshold: 1.0), counter, strategy);
        context.AddUserMessage("old");
        context.RecordModelResponse([new ToolUseContent("call", "read", "{}")]);
        context.RecordToolResult("call", "read", new string('r', 500));
        context.RecordModelResponse([new TextContent(new string('m', 800))]);
        context.AddUserMessage(new string('u', 300));

        // Act
        var result = await context.PrepareAsync();

        // Assert
        result.ToolResultsMasked.Should().Be(1);
        result.MessagesDropped.Should().Be(4);
        result.TokenBreakdown.Masked.Should().Be(0);
        result.TokenBreakdown.Other.Should().Be(300);
        result.Messages.Should().ContainSingle();
        Total(result.TokenBreakdown).Should().Be(result.TokensAfterCompaction);
    }

    [Fact]
    public async Task PrepareAsync_WhenSummaryWins_DiscardsTrialMaskingCounts()
    {
        // Arrange
        var counter = new LengthCounter();
        var summary = new LlmSummarizationStrategy(new SummaryModel("summary"), counter, new LlmSummarizationOptions(1, 10, 100));
        var strategy = new TieredCompactionStrategy(counter, new SlidingWindowOptions(1, 0.5), summary);
        using var context = new ConversationContext(new ContextBudget(1_000), counter, strategy);
        context.AddUserMessage(new string('u', 800));
        context.RecordModelResponse([new ToolUseContent("call", "read", "{}")]);
        context.RecordToolResult("call", "read", new string('r', 500));
        context.RecordModelResponse([new TextContent(new string('m', 800))]);
        context.AddUserMessage("next");

        // Act
        var result = await context.PrepareAsync();

        // Assert
        result.SummarizationStatus.Should().Be(SummarizationStatus.NewSummary);
        result.ToolResultsMasked.Should().Be(0);
        result.MessagesSummarized.Should().Be(4);
        result.TokenBreakdown.Masked.Should().Be(0);
    }

    [Fact]
    public async Task PrepareAsync_WhenMixedMessagesAreMasked_UsesWholeMessageCategoryPrecedence()
    {
        // Arrange
        var counter = new LengthCounter();
        var strategy = new SlidingWindowStrategy(counter, new SlidingWindowOptions(1, 0.5) { ExemptToolNames = ["notes"] });
        using var context = new ConversationContext(new ContextBudget(1_000), counter, strategy);
        context.AddPinnedMessage(MessageRole.System, [new TextContent("pin"), new ToolResultContent("pin", "notes", "keep")]);
        context.RecordModelResponse([
            new TextContent("explanation"), new ToolResultContent("a", "read", new string('a', 600)),
            new ToolResultContent("b", "notes", "durable")]);
        context.AddUserMessage(new string('u', 300));

        // Act
        var result = await context.PrepareAsync();

        // Assert
        result.ToolResultsMasked.Should().Be(1);
        result.TokenBreakdown.Pinned.Should().Be(7);
        result.TokenBreakdown.ToolResults.Should().Be(0);
        var mixed = result.Messages.Single(message => message.State == CompactionState.Masked);
        result.TokenBreakdown.Masked.Should().Be(counter.Count(mixed));
        mixed.Segments.Should().Contain(new ToolResultContent("b", "notes", "durable"));
        Total(result.TokenBreakdown).Should().Be(result.TokensAfterCompaction);
    }

    [Fact]
    public async Task PrepareAsync_WhenReturnedSegmentsAreMutated_RetainsIndependentPreviousPayload()
    {
        // Arrange
        var counter = new LengthCounter();
        using var context = new ConversationContext(new ContextBudget(1_000), counter, new SlidingWindowStrategy(counter));
        context.AddUserMessage("first");
        context.AddUserMessage("second");
        var first = await context.PrepareAsync();
        ((ContentSegment[])first.Messages[1].Segments)[0] = new TextContent("edited");

        // Act
        var next = await context.PrepareAsync();

        // Assert
        next.UnchangedPrefixMessages.Should().Be(1);
        next.UnchangedPrefixTokens.Should().Be(5);
    }

    [Fact]
    public async Task PrepareAsync_WhenSystemPromptIsReplaced_ReportsNoUnchangedPrefix()
    {
        // Arrange
        var counter = new LengthCounter();
        using var context = new ConversationContext(new ContextBudget(1_000), counter, new SlidingWindowStrategy(counter));
        context.SetSystemPrompt("first instruction");
        context.AddUserMessage("question");
        await context.PrepareAsync();
        context.SetSystemPrompt("other instruction");

        // Act
        var next = await context.PrepareAsync();

        // Assert
        next.UnchangedPrefixMessages.Should().Be(0);
        next.UnchangedPrefixTokens.Should().Be(0);
    }

    [Fact]
    public async Task PrepareAsync_WithALegacyCustomStrategy_ComputesViewDiagnosticsAndDefaultsStageDetails()
    {
        // Arrange
        var counter = new LengthCounter();
        using var context = new ConversationContext(new ContextBudget(100), counter, new LegacyStrategy());
        context.AddUserMessage(new string('u', 90));
        var first = await context.PrepareAsync();
        ((ContextMessage[])first.Messages)[0] = ContextMessage.FromText(MessageRole.User, "caller edited the returned list");

        // Act
        var next = await context.PrepareAsync();

        // Assert
        next.UnchangedPrefixMessages.Should().Be(1);
        next.UnchangedPrefixTokens.Should().Be(90);
        next.TokenBreakdown.Other.Should().Be(90);
        next.ToolResultsMasked.Should().Be(0);
        next.MessagesSummarized.Should().Be(0);
        next.SummarizationStatus.Should().Be(SummarizationStatus.NotNeeded);
    }

    [Fact]
    public async Task PrepareAsync_WhenOnlyToolIdentityChanges_StopsPrefixAtTheChangedMessage()
    {
        // Arrange
        var counter = new LengthCounter();
        using var context = new ConversationContext(new ContextBudget(1_000), counter, new SlidingWindowStrategy(counter));
        context.AddUserMessage("start");
        context.RecordModelResponse([new ToolUseContent("first", "read", "{}")]);
        var first = await context.PrepareAsync();
        ((ContentSegment[])first.Messages[1].Segments)[0] = new ToolUseContent("other", "read", "{}");

        // Act
        var next = await context.PrepareAsync();

        // Assert
        next.UnchangedPrefixMessages.Should().Be(1);
        next.UnchangedPrefixTokens.Should().Be(5);
    }

    [Fact]
    public async Task PrepareAsync_WhenCounterEstimatesZero_AssignsProviderCorrectionToFirstMessageCategory()
    {
        // Arrange
        var counter = new ZeroCounter();
        using var context = new ConversationContext(new ContextBudget(1_000), counter, new SlidingWindowStrategy(counter));
        context.SetSystemPrompt("instructions");
        context.AddUserMessage("question");
        await context.PrepareAsync();
        context.RecordModelResponse([new TextContent("answer")], providerInputTokens: 100);

        // Act
        var result = await context.PrepareAsync();

        // Assert
        result.TokensAfterCompaction.Should().Be(100);
        result.TokenBreakdown.Pinned.Should().Be(100);
        result.TokenBreakdown.Other.Should().Be(0);
        Total(result.TokenBreakdown).Should().Be(result.TokensAfterCompaction);
        result.UnchangedPrefixTokens.Should().Be(100);
    }

    private static ConversationContext CreateSummaryContext(IHistorySummarizer summarizer)
    {
        var counter = new LengthCounter();
        var summary = new LlmSummarizationStrategy(summarizer, counter, new LlmSummarizationOptions(1, 10, 100));
        var strategy = new TieredCompactionStrategy(counter, new SlidingWindowOptions(1, 0.5), summary);
        var context = new ConversationContext(new ContextBudget(1_000), counter, strategy);
        for (var i = 0; i < 4; i++)
            context.AddUserMessage(new string('u', 300));
        context.AddUserMessage("latest");
        return context;
    }

    private static int Total(PreparedTokenBreakdown breakdown) =>
        breakdown.Pinned + breakdown.Summary + breakdown.Masked + breakdown.ToolResults + breakdown.Other;

    /// <summary>Provides a deterministic token model at the counting boundary.</summary>
    private sealed class LengthCounter : ITokenCounter
    {
        /// <inheritdoc />
        public int Count(ContextMessage contextMessage) => contextMessage.Segments.Sum(segment => segment.Content.Length);

        /// <inheritdoc />
        public int Count(IEnumerable<ContextMessage> messages) => messages.Sum(this.Count);
    }

    /// <summary>Provides a custom counter whose estimates are all zero.</summary>
    private sealed class ZeroCounter : ITokenCounter
    {
        /// <inheritdoc />
        public int Count(ContextMessage contextMessage) => 0;

        /// <inheritdoc />
        public int Count(IEnumerable<ContextMessage> messages) => 0;
    }

    /// <summary>Represents a consumer strategy using only the original result constructor.</summary>
    private sealed class LegacyStrategy : ICompactionStrategy
    {
        /// <inheritdoc />
        public Task<CompactionResult> CompactAsync(
            IReadOnlyList<ContextMessage> messages, int availableTokens, CancellationToken cancellationToken = default)
        {
            var recreated = messages.Select(message => new ContextMessage { Role = message.Role, Segments = message.Segments }).ToArray();
            return Task.FromResult(new CompactionResult(recreated, 90, 90, 0, "legacy"));
        }
    }

    /// <summary>Provides deterministic summary text or a provider failure.</summary>
    private sealed class SummaryModel(string text, bool throws = false) : IHistorySummarizer
    {
        /// <inheritdoc />
        public Task<string> SummarizeAsync(
            IReadOnlyList<ContextMessage> messages, int targetTokens, int requestTokenBudget, CancellationToken cancellationToken = default)
        {
            if (throws)
                throw new InvalidOperationException("provider failed");
            return Task.FromResult(text);
        }
    }
}
