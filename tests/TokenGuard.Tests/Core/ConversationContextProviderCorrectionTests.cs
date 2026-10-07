using FluentAssertions;
using TokenGuard.Core;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Configuration;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;
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
}
