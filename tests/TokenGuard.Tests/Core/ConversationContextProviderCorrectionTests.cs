using FluentAssertions;
using TokenGuard.Core;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Configuration;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;
using TokenGuard.Core.Options;
using TokenGuard.Core.Strategies;
using TokenGuard.Tests.Diagnostics;

namespace TokenGuard.Tests.Core;

public sealed class ConversationContextProviderCorrectionTests
{
    [Fact]
    public async Task PrepareAsync_WhenTheProviderMeasuredAnUnchangeablePayloadAboveTheMaximum_ReturnsCannotCompact()
    {
        // Arrange
        using var context = CreateContext(StubCompactionStrategy.Unchanged());
        context.AddUserMessage(Text(700));
        await context.PrepareAsync();
        context.RecordModelResponse([new TextContent(Text(5))], providerInputTokens: 1_300);

        // Act
        var result = await context.PrepareAsync();

        // Assert
        // The messages sum to 705 and the correction is 1,300 - 700 = 600.
        result.Outcome.Should().Be(PrepareOutcome.CannotCompact);
        result.TokensBeforeCompaction.Should().Be(1_305);
        result.TokensAfterCompaction.Should().Be(1_305);
        result.MessagesCompacted.Should().Be(0);
        result.BudgetFailureReason.Should().Contain("1305 tokens > 1050 max");
        result.Messages.Should().Equal(context.History);
    }

    [Fact]
    public async Task PrepareAsync_WithTheDefaultPipeline_WhenTheProviderMeasuredThePayloadAboveTheMaximum_ReturnsCannotCompact()
    {
        // Arrange
        using var context = new ConversationContextFactory(new ConversationConfigBuilder().WithMaxTokens(1_000).Build()).Create();
        context.AddUserMessage(string.Join(' ', Enumerable.Repeat("word", 700)));
        var first = await context.PrepareAsync();
        context.RecordModelResponse([new TextContent("Done.")], providerInputTokens: 1_300);

        // Act
        var result = await context.PrepareAsync();

        // Assert
        first.Outcome.Should().Be(PrepareOutcome.Ready);
        first.TokensAfterCompaction.Should().BeInRange(650, 750);
        result.Outcome.Should().Be(PrepareOutcome.CannotCompact);
        result.Messages.Should().Equal(context.History);
        result.TokensAfterCompaction.Should().Be(result.TokensBeforeCompaction);
        result.TokensAfterCompaction.Should().Be(context.History.Sum(message => message.TokenCount ?? 0) + 1_300 - first.TokensAfterCompaction);
    }

    [Fact]
    public async Task PrepareAsync_WhenTheStrategyChangesNoMessages_ReportsEqualTokensBeforeAndAfter()
    {
        // Arrange
        using var context = CreateContext(StubCompactionStrategy.Unchanged());
        context.AddUserMessage(Text(500));
        await context.PrepareAsync();
        context.RecordModelResponse([new TextContent(Text(10))], providerInputTokens: 800);

        // Act
        var result = await context.PrepareAsync();

        // Assert
        // The messages sum to 510 and the correction is 800 - 500 = 300, which reaches the trigger of 800.
        result.Outcome.Should().Be(PrepareOutcome.Ready);
        result.TokensBeforeCompaction.Should().Be(810);
        result.TokensAfterCompaction.Should().Be(810);
    }

    [Fact]
    public async Task PrepareAsync_WhenBelowTheTriggerWithACorrection_ReportsEqualTokensBeforeAndAfter()
    {
        // Arrange
        using var context = CreateContext(StubCompactionStrategy.Unchanged());
        context.AddUserMessage(Text(100));
        await context.PrepareAsync();
        context.RecordModelResponse([new TextContent(Text(10))], providerInputTokens: 150);

        // Act
        var result = await context.PrepareAsync();

        // Assert
        result.Outcome.Should().Be(PrepareOutcome.Ready);
        result.TokensBeforeCompaction.Should().Be(160);
        result.TokensAfterCompaction.Should().Be(160);
    }

    [Fact]
    public async Task PrepareAsync_WhenCompactionRemovesMessages_ScalesTheCorrectionToTheMessagesThatRemain()
    {
        // Arrange
        using var context = await CreateCorrectedConversationAsync(StubCompactionStrategy.KeepNewest(2));

        // Act
        var result = await context.PrepareAsync();

        // Assert
        // Before: 650 + 300. After: the kept messages sum to 250, and the correction is 300 * 250 / 600 = 125.
        result.Outcome.Should().Be(PrepareOutcome.Compacted);
        result.TokensBeforeCompaction.Should().Be(950);
        result.Messages.Sum(message => message.TokenCount ?? 0).Should().Be(250);
        result.TokensAfterCompaction.Should().Be(375);
    }

    [Fact]
    public async Task PrepareAsync_WhenNoProviderReportArrivesAfterACompaction_AppliesTheSameCorrectionAgain()
    {
        // Arrange
        using var context = await CreateCorrectedConversationAsync(StubCompactionStrategy.KeepNewest(2));
        await context.PrepareAsync();
        context.AddUserMessage(Text(10));

        // Act
        var result = await context.PrepareAsync();

        // Assert
        // Before: 660 + 300. After: the kept messages sum to 60, and the correction is 300 * 60 / 600 = 30.
        result.TokensBeforeCompaction.Should().Be(960);
        result.TokensAfterCompaction.Should().Be(90);
    }

    [Fact]
    public async Task PrepareAsync_WhenAProviderReportArrivesAfterACompaction_ReplacesTheCorrection()
    {
        // Arrange
        using var context = await CreateCorrectedConversationAsync(StubCompactionStrategy.KeepNewest(2));
        await context.PrepareAsync();
        context.RecordModelResponse([new TextContent(Text(20))], providerInputTokens: 400);

        // Act
        var result = await context.PrepareAsync();

        // Assert
        // The provider measured the compacted payload of 250 at 400, so the correction is 150.
        // Before: 670 + 150. After: the kept messages sum to 70, and the correction is 150 * 70 / 250 = 42.
        result.TokensBeforeCompaction.Should().Be(820);
        result.TokensAfterCompaction.Should().Be(112);
    }

    [Fact]
    public async Task PrepareAsync_WhenTheScaledCorrectionKeepsACompactedPayloadAboveTheMaximum_ReturnsCompactionInsufficient()
    {
        // Arrange
        using var context = CreateContext(StubCompactionStrategy.KeepNewest(2), emergencyThreshold: null);
        context.AddUserMessage(Text(350));
        context.RecordModelResponse([new TextContent(Text(50))]);
        context.AddUserMessage(Text(350));
        await context.PrepareAsync();
        context.RecordModelResponse([new TextContent(Text(40))], providerInputTokens: 2_250);

        // Act
        var result = await context.PrepareAsync();

        // Assert
        // The kept messages sum to 390, inside the maximum. The correction is 2,250 - 750 = 1,500, scaled to 1,500 * 390 / 750 = 780.
        result.Outcome.Should().Be(PrepareOutcome.CompactionInsufficient);
        result.TokensAfterCompaction.Should().Be(1_170);
        result.BudgetFailureReason.Should().Contain("1170 tokens > 1050 max");
    }

    [Fact]
    public async Task PrepareAsync_WhenEmergencyTruncationRunsWithACorrection_StopsOnceTheCorrectedTotalFits()
    {
        // Arrange
        using var context = CreateContext(StubCompactionStrategy.Unchanged(), compactionThreshold: 0.5);
        context.AddUserMessage(Text(600));
        await context.PrepareAsync();
        context.RecordModelResponse([new TextContent(Text(100))], providerInputTokens: 1_500);
        context.AddUserMessage(Text(150));
        context.RecordModelResponse([new TextContent(Text(50))]);
        context.AddUserMessage(Text(100));

        // Act
        var result = await context.PrepareAsync();

        // Assert
        // The correction is 1,500 - 600 = 900 and the messages sum to 1,000. Dropping the first turn leaves 300,
        // which totals 300 + 900 * 300 / 600 = 750 and fits the emergency trigger of 1,000.
        result.TokensBeforeCompaction.Should().Be(1_900);
        result.MessagesDropped.Should().Be(2);
        result.Messages.Should().Equal(context.History.Skip(2));
        result.TokensAfterCompaction.Should().Be(750);
        result.Outcome.Should().Be(PrepareOutcome.Compacted);
    }

    [Fact]
    public async Task PrepareAsync_WhenNoProviderReportWasRecorded_ReportsTheSummedMessageEstimates()
    {
        // Arrange
        using var context = CreateContext(StubCompactionStrategy.KeepNewest(1));
        context.AddUserMessage(Text(500));
        context.RecordModelResponse([new TextContent(Text(100))]);
        context.AddUserMessage(Text(300));

        // Act
        var result = await context.PrepareAsync();

        // Assert
        result.TokensBeforeCompaction.Should().Be(900);
        result.TokensAfterCompaction.Should().Be(300);
        result.Outcome.Should().Be(PrepareOutcome.Compacted);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task PrepareAsync_WhenTheProviderReportIsZeroOrLess_StillCompactsAHistoryAboveTheTrigger(int providerInputTokens)
    {
        // Arrange
        using var context = CreateContext(StubCompactionStrategy.KeepNewest(2));
        context.AddUserMessage(Text(300));
        context.RecordModelResponse([new TextContent(Text(100))]);
        context.AddUserMessage(Text(450));
        await context.PrepareAsync();
        context.RecordModelResponse([new TextContent(Text(50))], providerInputTokens);

        // Act
        var result = await context.PrepareAsync();

        // Assert
        // The messages sum to 900, above the trigger of 800, and no correction was learned.
        result.Outcome.Should().Be(PrepareOutcome.Compacted);
        result.TokensBeforeCompaction.Should().Be(900);
        result.TokensAfterCompaction.Should().Be(500);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task PrepareAsync_WhenAProviderReportOfZeroOrLessFollowsACorrection_AppliesTheSameCorrectionAgain(int providerInputTokens)
    {
        // Arrange
        using var context = await CreateCorrectedConversationAsync(StubCompactionStrategy.Unchanged());
        await context.PrepareAsync();
        context.RecordModelResponse([new TextContent(Text(10))], providerInputTokens);

        // Act
        var result = await context.PrepareAsync();

        // Assert
        // The messages sum to 660 and the correction stays 300.
        result.TokensBeforeCompaction.Should().Be(960);
    }

    [Fact]
    public async Task PrepareAsync_WhenAPositiveProviderReportFollowsAReportOfZero_ReplacesTheCorrection()
    {
        // Arrange
        using var context = await CreateCorrectedConversationAsync(StubCompactionStrategy.Unchanged());
        await context.PrepareAsync();
        context.RecordModelResponse([new TextContent(Text(10))], providerInputTokens: 0);
        await context.PrepareAsync();
        context.RecordModelResponse([new TextContent(Text(10))], providerInputTokens: 700);

        // Act
        var result = await context.PrepareAsync();

        // Assert
        // The messages sum to 670 and the correction is 700 - 660 = 40.
        result.TokensBeforeCompaction.Should().Be(710);
    }

    [Theory]
    [InlineData(700, 1_122)]
    [InlineData(1_100, 746)]
    public async Task PrepareAsync_AfterAProviderReport_PassesTheMaximumScaledByEstimateOverCorrectedHistoryMinusThePinnedEstimate(
        int providerInputTokens, int expectedAvailableTokens)
    {
        // Arrange
        var budgets = new List<int>();
        using var context = CreateContext(new StubCompactionStrategy((messages, availableTokens) =>
        {
            budgets.Add(availableTokens);
            return new CompactionResult(messages, 0, 0, 0, StubCompactionStrategy.Name);
        }));
        context.AddPinnedMessage(MessageRole.User, Text(100));
        context.AddUserMessage(Text(800));
        await context.PrepareAsync();
        context.RecordModelResponse([new TextContent(Text(200))], providerInputTokens);

        // Act
        await context.PrepareAsync();

        // Assert
        // The history estimate is 1,100 and the correction is the report minus 900, so the budget is
        // floor(1,000 × 1,100 / corrected history) − 100 for the pinned message.
        budgets.Should().Equal(900, expectedAvailableTokens);
    }

    [Fact]
    public async Task PrepareAsync_AfterAProviderReport_PassesTheUncorrectedBudgetForTheMaskingStepLines()
    {
        // Arrange
        var strategy = new SteppedBudgetRecorder();
        using var context = CreateContext(strategy);
        context.AddPinnedMessage(MessageRole.User, Text(100));
        context.AddUserMessage(Text(800));
        await context.PrepareAsync();
        context.RecordModelResponse([new TextContent(Text(200))], providerInputTokens: 700);

        // Act
        await context.PrepareAsync();

        // Assert
        strategy.Budgets.Should().Equal((900, 900), (1_122, 900));
    }

    [Fact]
    public async Task PrepareAsync_WhenTheEstimateFitsButTheCorrectedHistoryDoesNot_SummarizesInsteadOfTruncating()
    {
        // Arrange
        var summarizer = ScriptedSummarizer.Returning(Text(50));
        var counter = new TextLengthTokenCounter();
        var summarization = new LlmSummarizationStrategy(
            summarizer, counter, new LlmSummarizationOptions(windowSize: 2, minSummaryTokens: 5, maxSummaryTokens: 100));
        using var context = CreateContext(new TieredCompactionStrategy(counter, new SlidingWindowOptions(windowSize: 2), summarization));
        context.AddUserMessage(Text(300));
        await context.PrepareAsync();
        context.RecordModelResponse([new TextContent(Text(100))], providerInputTokens: 600);
        context.AddUserMessage(Text(300));
        context.RecordModelResponse([new TextContent(Text(100))]);
        context.AddUserMessage(Text(100));

        // Act
        var result = await context.PrepareAsync();

        // Assert
        // The history estimate of 900 fits the maximum of 1,000, but with the correction of 300 it is 1,200.
        summarizer.Calls.Should().Be(1);
        result.MessagesDropped.Should().Be(0);
        result.Messages.Should().Contain(message => message.State == CompactionState.Summarized);
    }

    // The messages sum to 650, and the correction is 900 - 600 = 300, learned on a payload of 600.
    private static async Task<ConversationContext> CreateCorrectedConversationAsync(ICompactionStrategy strategy)
    {
        var context = CreateContext(strategy);
        context.AddUserMessage(Text(300));
        context.RecordModelResponse([new TextContent(Text(100))]);
        context.AddUserMessage(Text(200));
        await context.PrepareAsync();
        context.RecordModelResponse([new TextContent(Text(50))], providerInputTokens: 900);

        return context;
    }

    private static ConversationContext CreateContext(
        ICompactionStrategy strategy, double compactionThreshold = 0.8, double? emergencyThreshold = 1.0)
    {
        var budget = new ContextBudget(1_000, compactionThreshold, emergencyThreshold, overrunTolerance: 0.05);
        return new ConversationContext(budget, new TextLengthTokenCounter(), strategy);
    }

    private static string Text(int length) => new('a', length);

    /// <summary>
    ///     Represents a stepped strategy that records both budgets of each call and returns its input unchanged.
    /// </summary>
    private sealed class SteppedBudgetRecorder : ISteppedCompactionStrategy
    {
        /// <summary>
        ///     Gets the available budget and the step budget of each call, oldest first.
        /// </summary>
        public List<(int Available, int Step)> Budgets { get; } = [];

        /// <inheritdoc />
        public Task<CompactionResult> CompactAsync(
            IReadOnlyList<ContextMessage> messages, int availableTokens, int stepBudgetTokens, CancellationToken cancellationToken = default)
        {
            this.Budgets.Add((availableTokens, stepBudgetTokens));
            return Task.FromResult(new CompactionResult(messages, 0, 0, 0, StubCompactionStrategy.Name));
        }

        /// <inheritdoc />
        public Task<CompactionResult> CompactAsync(
            IReadOnlyList<ContextMessage> messages, int availableTokens, CancellationToken cancellationToken = default) =>
            this.CompactAsync(messages, availableTokens, availableTokens, cancellationToken);
    }
}
