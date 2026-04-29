using FluentAssertions;
using TokenGuard.Core;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Exceptions;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;

namespace TokenGuard.Tests.Core;

public sealed class ConversationContextTests
{
    [Fact]
    public void ContextMessage_WithPinnedFlag_RetainsValueAndDefaultsToFalse()
    {
        // Arrange
        var pinned = ContextMessage.FromText(MessageRole.User, "pinned") with { IsPinned = true };
        var unpinned = ContextMessage.FromText(MessageRole.User, "unpinned");

        // Act

        // Assert
        pinned.IsPinned.Should().BeTrue();
        unpinned.IsPinned.Should().BeFalse();
    }

    [Fact]
    public void ContextMessage_WithExpression_PreservesPinnedFlagUnlessOverridden()
    {
        // Arrange
        var original = ContextMessage.FromText(MessageRole.User, "pinned") with { IsPinned = true };

        // Act
        var preserved = original with { State = CompactionState.Masked };
        var overridden = original with { IsPinned = false };

        // Assert
        preserved.IsPinned.Should().BeTrue();
        overridden.IsPinned.Should().BeFalse();
    }

    [Fact]
    public async Task PrepareAsync_WhenEstimateIsBelowThreshold_ReturnsOriginalListAndSkipsCompaction()
    {
        // Arrange
        var counter = new TrackingTokenCounter();
        var strategy = new TrackingCompactionStrategy();
        var engine = new ConversationContext(ContextBudget.For(1_000), counter, strategy);

        engine.AddUserMessage("hello");
        counter.Set(engine.History[0], 0);

        // Act
        var result = await engine.PrepareAsync();
        var prepared = result.Messages;

        // Assert
        prepared.Should().BeSameAs(engine.History);
        strategy.CompactCalls.Should().Be(0);
    }

    [Fact]
    public async Task PrepareAsync_WhenEstimateMeetsThreshold_UsesCompactionStrategyResult()
    {
        // Arrange
        var compacted = ContextMessage.FromText(MessageRole.Model, "compacted");
        var counter = new TrackingTokenCounter();

        counter.SetByText("original", 800);
        counter.Set(compacted, 800);

        var strategy = new TrackingCompactionStrategy(new CompactionResult([compacted], 800, 800, 1, "TestStrategy"));
        var engine = new ConversationContext(ContextBudget.For(1_000), counter, strategy);

        engine.AddUserMessage("original");

        // Act
        var result = await engine.PrepareAsync();
        var prepared = result.Messages;

        // Assert
        strategy.CompactCalls.Should().Be(1);
        strategy.LastInput.Should().ContainSingle().Which.Should().BeSameAs(engine.History[0]);
        prepared.Should().ContainSingle().Which.Should().BeSameAs(compacted);
    }

    [Fact]
    public async Task PrepareAsync_WhenSystemMessagesExist_KeepsThemAtTopAndAdjustsBudget()
    {
        // Arrange
        var compacted = ContextMessage.FromText(MessageRole.Model, "compacted");
        var counter = new TrackingTokenCounter();

        counter.SetByText("sys1", 100);
        counter.SetByText("user1", 900);
        counter.Set(compacted, 100);

        var strategy = new TrackingCompactionStrategy(new CompactionResult([compacted], 900, 100, 1, "TestStrategy"));
        var budget = ContextBudget.For(1_000);
        var engine = new ConversationContext(budget, counter, strategy);

        engine.SetSystemPrompt("sys1");
        engine.AddUserMessage("user1");

        var sys1 = engine.History[0];
        var user1 = engine.History[1];

        // Act
        var result = await engine.PrepareAsync();
        var prepared = result.Messages;

        // Assert
        strategy.LastInput.Should().ContainSingle().Which.Should().BeSameAs(user1);
        strategy.LastAvailableTokens.Should().Be(900);
        prepared.Should().HaveCount(2);
        prepared[0].Should().BeSameAs(sys1);
        prepared[1].Should().BeSameAs(compacted);
    }

    [Fact]
    public async Task SetSystemPrompt_InsertsAtTopAndPrepareAsyncReturnsCorrectOrder()
    {
        // Arrange
        var counter = new TrackingTokenCounter();
        var strategy = new TrackingCompactionStrategy();
        var engine = new ConversationContext(ContextBudget.For(1_000), counter, strategy);

        engine.AddUserMessage("user1");
        engine.SetSystemPrompt("sys1");

        var sys1 = engine.History.First(m => m.Role == MessageRole.System);
        var user1 = engine.History.First(m => m.Role == MessageRole.User);

        counter.Set(sys1, 100);
        counter.Set(user1, 100);

        // Act
        var result = await engine.PrepareAsync();
        var prepared = result.Messages;

        // Assert
        prepared.Should().HaveCount(2);
        prepared[0].Should().BeSameAs(sys1);
        strategy.CompactCalls.Should().Be(0);
    }

    [Fact]
    public void AddPinnedMessage_WithText_AppendsPinnedMessageToHistory()
    {
        // Arrange
        var engine = new ConversationContext(ContextBudget.For(1_000), new TrackingTokenCounter(), new TrackingCompactionStrategy());

        // Act
        engine.AddPinnedMessage(MessageRole.User, "durable");

        // Assert
        engine.History.Should().ContainSingle();
        engine.History[0].Role.Should().Be(MessageRole.User);
        engine.History[0].IsPinned.Should().BeTrue();
        AssertText(engine.History[0], "durable");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddPinnedMessage_WithInvalidText_ThrowsArgumentException(string? text)
    {
        // Arrange
        var engine = new ConversationContext(ContextBudget.For(1_000), new TrackingTokenCounter(), new TrackingCompactionStrategy());

        // Act
        Action act = () => engine.AddPinnedMessage(MessageRole.User, text!);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddPinnedMessage_WithNullContent_ThrowsArgumentNullException()
    {
        // Arrange
        var engine = new ConversationContext(ContextBudget.For(1_000), new TrackingTokenCounter(), new TrackingCompactionStrategy());

        // Act
        Action act = () => engine.AddPinnedMessage(MessageRole.User, (IEnumerable<ContentSegment>)null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddPinnedMessage_WithEmptyContent_ThrowsArgumentException()
    {
        // Arrange
        var engine = new ConversationContext(ContextBudget.For(1_000), new TrackingTokenCounter(), new TrackingCompactionStrategy());

        // Act
        Action act = () => engine.AddPinnedMessage(MessageRole.User, Array.Empty<ContentSegment>());

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SetSystemPrompt_ProducesPinnedMessage()
    {
        // Arrange
        var engine = new ConversationContext(ContextBudget.For(1_000), new TrackingTokenCounter(), new TrackingCompactionStrategy());

        // Act
        engine.SetSystemPrompt("system");

        // Assert
        engine.History.Should().ContainSingle();
        engine.History[0].Role.Should().Be(MessageRole.System);
        engine.History[0].IsPinned.Should().BeTrue();
        AssertText(engine.History[0], "system");
    }

    [Fact]
    public async Task SetSystemPrompt_WhenReplacingExistingPrompt_ReservedTokensUseNewPinnedTotalOnly()
    {
        // Arrange
        var counter = new TrackingTokenCounter();
        counter.SetByText("sys-old", 120);
        counter.SetByText("sys-new", 40);
        counter.SetByText("user", 800);

        var compacted = ContextMessage.FromText(MessageRole.Model, "compacted");
        counter.Set(compacted, 100);

        var strategy = new TrackingCompactionStrategy(new CompactionResult([compacted], 800, 100, 1, "TestStrategy"));
        var engine = new ConversationContext(ContextBudget.For(1_000), counter, strategy);

        engine.SetSystemPrompt("sys-old");
        engine.SetSystemPrompt("sys-new");
        engine.AddUserMessage("user");

        // Act
        var result = await engine.PrepareAsync();
        var prepared = result.Messages;

        // Assert
        strategy.CompactCalls.Should().Be(1);
        strategy.LastAvailableTokens.Should().Be(960);
        prepared[0].IsPinned.Should().BeTrue();
        AssertText(prepared[0], "sys-new");
    }

    [Fact]
    public async Task PrepareAsync_WhenCalledTwiceWithoutHistoryChange_DoesNotAdvanceTurnForSubsequentMessages()
    {
        // Arrange
        var engine = new ConversationContext(ContextBudget.For(1_000), new TrackingTokenCounter(), new TrackingCompactionStrategy());

        engine.AddUserMessage("user");
        var userMessage = engine.History[0];

        // Act
        _ = await engine.PrepareAsync();
        _ = await engine.PrepareAsync();

        engine.RecordModelResponse([new TextContent("model")]);
        engine.RecordToolResult("tool-1", "weather", "sunny");

        var modelMessage = engine.History[1];
        var toolMessage = engine.History[2];

        // Assert
        userMessage.Turn.Should().Be(0);
        modelMessage.Turn.Should().Be(1);
        toolMessage.Turn.Should().Be(1);
    }

    [Fact]
    public async Task SetSystemPrompt_WhenReplacingExistingPrompt_AdvancesTurnAfterNextPrepare()
    {
        // Arrange
        var engine = new ConversationContext(ContextBudget.For(1_000), new TrackingTokenCounter(), new TrackingCompactionStrategy());

        engine.SetSystemPrompt("sys-old");

        // Act
        _ = await engine.PrepareAsync();

        engine.SetSystemPrompt("sys-new");
        _ = await engine.PrepareAsync();

        engine.AddUserMessage("after-replace");

        var systemMessage = engine.History[0];
        var userMessage = engine.History[1];

        // Assert
        AssertText(systemMessage, "sys-new");
        systemMessage.Turn.Should().Be(0);
        userMessage.Turn.Should().Be(2);
    }

    [Fact]
    public async Task PrepareAsync_WhenPinnedMessagesExist_PartitionsForStrategyAndAdjustsReservedBudget()
    {
        // Arrange
        var counter = new TrackingTokenCounter();
        counter.SetByText("p0", 100);
        counter.SetByText("u1", 250);
        counter.SetByText("u2", 250);
        counter.SetByText("p3", 150);
        counter.SetByText("u4", 250);

        var compactedU1 = ContextMessage.FromText(MessageRole.User, "c1");
        var compactedU2 = ContextMessage.FromText(MessageRole.User, "c2");
        var compactedU4 = ContextMessage.FromText(MessageRole.User, "c4");
        counter.Set(compactedU1, 200);
        counter.Set(compactedU2, 200);
        counter.Set(compactedU4, 200);

        var strategy = new TrackingCompactionStrategy(new CompactionResult(
            [compactedU1, compactedU2, compactedU4],
            750,
            600,
            2,
            "TestStrategy"));

        var engine = new ConversationContext(ContextBudget.For(1_000), counter, strategy);
        engine.AddPinnedMessage(MessageRole.System, "p0");
        engine.AddUserMessage("u1");
        engine.AddUserMessage("u2");
        engine.AddPinnedMessage(MessageRole.User, "p3");
        engine.AddUserMessage("u4");

        // Act
        var result = await engine.PrepareAsync();
        var prepared = result.Messages;

        // Assert
        strategy.LastInput.Should().HaveCount(3);
        strategy.LastInput.Should().OnlyContain(message => !message.IsPinned);
        strategy.LastInput.Select(GetText).Should().Equal("u1", "u2", "u4");
        strategy.LastAvailableTokens.Should().Be(750);
        prepared.Select(GetText).Should().Equal("p0", "c1", "c2", "p3", "c4");
        prepared[0].IsPinned.Should().BeTrue();
        prepared[3].IsPinned.Should().BeTrue();
    }

    [Fact]
    public async Task PrepareAsync_WhenPinnedMessagesExceedAvailableBudget_ThrowsDiagnosticException()
    {
        // Arrange
        // MaxTokens = 1000. Pinned total = 1100 > 1000 → throws.
        var budget = new ContextBudget(1_000, 0.5);
        var counter = new TrackingTokenCounter();
        counter.SetByText("pin-a", 600);
        counter.SetByText("pin-b", 500);
        var engine = new ConversationContext(budget, counter, new TrackingCompactionStrategy());

        engine.AddPinnedMessage(MessageRole.System, "pin-a");
        engine.AddPinnedMessage(MessageRole.User, "pin-b");

        // Act
        Func<Task> act = () => engine.PrepareAsync();

        // Assert
        var ex = await act.Should().ThrowAsync<PinnedTokenBudgetExceededException>();
        ex.Which.PinnedTokenTotal.Should().Be(1_100);
        ex.Which.MaxTokens.Should().Be(1_000);
    }

    [Fact]
    public async Task PrepareAsync_WhenPinnedTokensExceedCompactionButNotEmergencyThreshold_RunsStrategyNormally()
    {
        // Arrange
        var budget = new ContextBudget(1_000, 0.5, 0.9);
        var counter = new TrackingTokenCounter();
        counter.SetByText("pin", 700);
        counter.SetByText("u1", 150);
        counter.SetByText("u2", 50);

        var strategy = new TrackingCompactionStrategy();
        var engine = new ConversationContext(budget, counter, strategy);

        engine.AddPinnedMessage(MessageRole.System, "pin");
        engine.AddUserMessage("u1");
        engine.AddUserMessage("u2");

        // Act
        var result = await engine.PrepareAsync();
        var prepared = result.Messages;

        // Assert
        strategy.CompactCalls.Should().Be(1);
        strategy.LastAvailableTokens.Should().Be(300);
        prepared.Select(GetText).Should().Equal("pin", "u1", "u2");
    }

    [Fact]
    public async Task PrepareAsync_WhenEmergencyTruncationRuns_PinnedMessagesAreNeverDropped()
    {
        // Arrange
        // Budget: compactionTrigger=500, emergencyTrigger=900.
        // u1 and u2 are added in separate turns (PrepareAsync is called between them) so they form
        // independent drop units. Dropping u1 alone (300 tokens) brings the total from 1150 to 850,
        // which falls below the emergency threshold, so u2 is preserved.
        var budget = new ContextBudget(1_000, 0.5, 0.9);
        var counter = new TrackingTokenCounter();
        counter.SetByText("pin-oldest", 100);
        counter.SetByText("u1", 300);
        counter.SetByText("u2", 300);
        counter.SetByText("pin-middle", 150);
        counter.SetByText("latest", 300);

        var strategy = new TrackingCompactionStrategy();
        var engine = new ConversationContext(budget, counter, strategy);

        engine.AddPinnedMessage(MessageRole.System, "pin-oldest");
        _ = await engine.PrepareAsync(); // advance turn; total=100, no compaction

        engine.AddUserMessage("u1");
        _ = await engine.PrepareAsync(); // advance turn; total=400, no compaction

        engine.AddUserMessage("u2");
        engine.AddPinnedMessage(MessageRole.User, "pin-middle");
        engine.AddUserMessage("latest");

        // Act
        var result = await engine.PrepareAsync(); // total=1150 > emergencyTrigger(900)
        var prepared = result.Messages;

        // Assert
        prepared.Select(GetText).Should().Equal("pin-oldest", "u2", "pin-middle", "latest");
        prepared.Should().OnlyContain(message => message.IsPinned || GetText(message) == "u2" || GetText(message) == "latest");
    }

    [Fact]
    public async Task PrepareAsync_WhenEmergencyTruncationDropsATurn_RemovesAllMessagesFromThatTurnAtomically()
    {
        // Arrange
        var budget = new ContextBudget(1_000, 0.5, 0.9);
        var counter = new TrackingTokenCounter();
        counter.SetByText("old-user", 100);
        counter.SetByText("old-model", 100);
        counter.SetByText("latest", 850);

        var engine = new ConversationContext(budget, counter, new TrackingCompactionStrategy());

        engine.AddUserMessage("old-user");
        engine.RecordModelResponse([new TextContent("old-model")]);
        engine.RecordToolResult("tool-1", "weather", "done");

        _ = await engine.PrepareAsync();

        engine.AddUserMessage("latest");

        var oldUserMessage = engine.History[0];
        var oldModelMessage = engine.History[1];
        var oldToolMessage = engine.History[2];
        var latestMessage = engine.History[3];

        // Act
        var result = await engine.PrepareAsync();
        var prepared = result.Messages;

        // Assert
        oldUserMessage.Turn.Should().Be(0);
        oldModelMessage.Turn.Should().Be(0);
        oldToolMessage.Turn.Should().Be(0);
        latestMessage.Turn.Should().Be(1);
        prepared.Should().ContainSingle().Which.Should().BeSameAs(latestMessage);
        prepared.Should().NotContain(oldUserMessage);
        prepared.Should().NotContain(oldModelMessage);
        prepared.Should().NotContain(oldToolMessage);
    }

    [Fact]
    public async Task PrepareAsync_WhenOldestMessageIsPinnedAndOverBudget_PreservesOldestPinnedMessage()
    {
        // Arrange
        var budget = new ContextBudget(1_000, 0.5, 0.9);
        var counter = new TrackingTokenCounter();
        counter.SetByText("pin-oldest", 300);
        counter.SetByText("older", 300);
        counter.SetByText("latest", 400);

        var strategy = new TrackingCompactionStrategy();
        var engine = new ConversationContext(budget, counter, strategy);

        engine.AddPinnedMessage(MessageRole.System, "pin-oldest");
        engine.AddUserMessage("older");
        engine.AddUserMessage("latest");

        // Act
        var result = await engine.PrepareAsync();
        var prepared = result.Messages;

        // Assert
        prepared.Select(GetText).Should().Equal("pin-oldest", "latest");
        prepared[0].IsPinned.Should().BeTrue();
    }

    [Fact]
    public async Task RecordModelResponse_PrewarmsCache_SoPrepareAsyncDoesNotRecountSameMessage()
    {
        // Arrange
        var counter = new TrackingTokenCounter();
        var strategy = new TrackingCompactionStrategy();
        var engine = new ConversationContext(ContextBudget.For(1_000), counter, strategy);

        engine.RecordModelResponse([new TextContent("hello")]);
        var message = engine.History[0];
        counter.Set(message, 1);

        // Act
        await engine.PrepareAsync();

        // Assert
        counter.GetCountCalls(message).Should().Be(1);
    }

    [Fact]
    public async Task RecordModelResponse_WithProviderInputTzokens_AppliesAnchorCorrectionOnNextPrepareAsync()
    {
        // Arrange
        var counter = new TrackingTokenCounter();
        var strategy = new TrackingCompactionStrategy(new CompactionResult([ContextMessage.FromText(MessageRole.Model, "compressed")], 800, 100, 1, "TestStrategy"));
        var engine = new ConversationContext(ContextBudget.For(1_000), counter, strategy);

        engine.AddUserMessage("hello");
        counter.Set(engine.History[0], 0);

        await engine.PrepareAsync();

        engine.RecordModelResponse([new TextContent("reply")], providerInputTokens: 800);
        counter.Set(engine.History[1], 0);

        // Act
        await engine.PrepareAsync();

        // Assert
        strategy.CompactCalls.Should().Be(1);
    }

    [Fact]
    public async Task RecordModelResponse_WithProviderTokensLessThanEstimate_DecreasesRunningTotal()
    {
        // Arrange
        var counter = new TrackingTokenCounter();
        var strategy = new TrackingCompactionStrategy();
        var engine = new ConversationContext(ContextBudget.For(1_000), counter, strategy);

        engine.AddUserMessage("hello");
        counter.Set(engine.History[0], 100);

        _ = await engine.PrepareAsync();

        engine.RecordModelResponse([new TextContent("reply")], providerInputTokens: 25);
        counter.Set(engine.History[1], 50);

        // Act
        var result = await engine.PrepareAsync();
        var prepared = result.Messages;

        // Assert
        prepared.Should().BeSameAs(engine.History);
        strategy.CompactCalls.Should().Be(0);
    }

    [Fact]
    public async Task RecordModelResponse_WithMultipleSequentialProviderCorrections_AccumulatesCorrectly()
    {
        // Arrange
        var counter = new TrackingTokenCounter();
        var strategy = new TrackingCompactionStrategy();
        var engine = new ConversationContext(ContextBudget.For(1_000), counter, strategy);

        engine.AddUserMessage("u1");
        counter.Set(engine.History[0], 100);

        var result = await engine.PrepareAsync();

        engine.RecordModelResponse([new TextContent("m1")], providerInputTokens: 80);
        counter.Set(engine.History[1], 50);

        var firstResult = await engine.PrepareAsync();
        var firstPrepared = firstResult.Messages;

        engine.RecordModelResponse([new TextContent("m2")], providerInputTokens: 90);
        counter.Set(engine.History[2], 20);

        var secondResult = await engine.PrepareAsync();
        var secondPrepared = secondResult.Messages;

        engine.RecordModelResponse([new TextContent("m3")], providerInputTokens: 95);
        counter.Set(engine.History[3], 10);

        // Act
        var thirdResult = await engine.PrepareAsync();
        var thirdPrepared = thirdResult.Messages;

        // Assert
        firstPrepared.Should().BeSameAs(engine.History);
        secondPrepared.Should().BeSameAs(engine.History);
        thirdPrepared.Should().BeSameAs(engine.History);
        strategy.CompactCalls.Should().Be(0);
    }

    [Fact]
    public async Task PrepareAsync_CacheUsesReferenceIdentity_ForEquivalentButDistinctMessages()
    {
        // Arrange
        var counter = new TrackingTokenCounter();
        var strategy = new TrackingCompactionStrategy();
        var engine = new ConversationContext(ContextBudget.For(1_000), counter, strategy);

        engine.AddUserMessage("same");
        engine.AddUserMessage("same");

        var first = engine.History[0];
        var second = engine.History[1];
        counter.Set(first, 0);
        counter.Set(second, 0);

        // Act
        var result = await engine.PrepareAsync();

        // Assert
        counter.GetCountCalls(first).Should().Be(1);
        counter.GetCountCalls(second).Should().Be(1);
    }

    [Fact]
    public async Task PrepareAsync_CachesTokenCountOnCompactedMessages_SoTheyAreNotRecounted()
    {
        // Arrange
        var compacted = ContextMessage.FromText(MessageRole.Model, "compacted");
        var counter = new TrackingTokenCounter();

        counter.SetByText("original", 800);
        counter.Set(compacted, 800);

        var strategy = new TrackingCompactionStrategy(new CompactionResult([compacted], 800, 800, 1, "TestStrategy"));
        var engine = new ConversationContext(ContextBudget.For(1_000), counter, strategy);

        engine.AddUserMessage("original");

        var result = await engine.PrepareAsync();

        // Act
        await engine.PrepareAsync();

        // Assert
        counter.GetCountCalls(compacted).Should().Be(1);
    }

    [Fact]
    public void Dispose_ClearsHistory()
    {
        // Arrange
        var counter = new TrackingTokenCounter();
        var strategy = new TrackingCompactionStrategy();
        var engine = new ConversationContext(ContextBudget.For(1_000), counter, strategy);

        engine.AddUserMessage("hello");
        engine.Dispose();

        // Assert
        Action act = () => _ = engine.History;
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        // Arrange
        var engine = new ConversationContext(ContextBudget.For(1_000), new TrackingTokenCounter(), new TrackingCompactionStrategy());

        // Act
        engine.Dispose();
        engine.Dispose();

        // Assert
    }

    [Theory]
    [InlineData("SetSystemPrompt")]
    [InlineData("AddPinnedMessage")]
    [InlineData("AddUserMessage")]
    [InlineData("RecordModelResponse")]
    [InlineData("RecordToolResult")]
    [InlineData("PrepareAsync")]
    public async Task PublicMembers_AfterDispose_ThrowObjectDisposedException(string member)
    {
        // Arrange
        var engine = new ConversationContext(ContextBudget.For(1_000), new TrackingTokenCounter(), new TrackingCompactionStrategy());
        engine.Dispose();

        // Act
        Func<Task> act = () => member switch
        {
            "SetSystemPrompt"     => Sync(() => engine.SetSystemPrompt("x")),
            "AddPinnedMessage"    => Sync(() => engine.AddPinnedMessage(MessageRole.User, "x")),
            "AddUserMessage"      => Sync(() => engine.AddUserMessage("x")),
            "RecordModelResponse" => Sync(() => engine.RecordModelResponse([new TextContent("x")])),
            "RecordToolResult"    => Sync(() => engine.RecordToolResult("id", "tool", "x")),
            "PrepareAsync"        => engine.PrepareAsync(),
            _                     => throw new ArgumentOutOfRangeException(nameof(member))
        };

        // Assert
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task PrepareAsync_WhenCompactionApplied_InvokesObserverExactlyOnceWithCorrectMetrics()
    {
        // Arrange
        var compacted = ContextMessage.FromText(MessageRole.Model, "compacted");
        var counter = new TrackingTokenCounter();
        counter.SetByText("original", 800);
        counter.Set(compacted, 100);

        var compactionResult = new CompactionResult([compacted], 800, 100, 3, "TestStrategy");
        var strategy = new TrackingCompactionStrategy(compactionResult);
        var observer = new TrackingCompactionObserver();
        var engine = new ConversationContext(ContextBudget.For(1_000), counter, strategy, observer);

        engine.AddUserMessage("original");

        // Act
        var result = await engine.PrepareAsync();

        // Assert
        observer.Events.Should().HaveCount(1);
        var evt = observer.Events[0];
        evt.Result.TokensBefore.Should().Be(800);
        evt.Result.TokensAfter.Should().Be(100);
        evt.Result.MessagesAffected.Should().Be(3);

        result.MessagesDropped.Should().Be(0);
    }

    [Fact]
    public async Task PrepareAsync_WhenEmergencyTruncationDropsMessages_ReportsExplicitStageDiagnostics()
    {
        // Arrange
        // Budget: compactionTrigger=500, emergencyTrigger=900
        var budget = new ContextBudget(1000, 0.5, 0.9);

        var olderMsg = ContextMessage.FromText(MessageRole.User, "older");
        var newerMsg = ContextMessage.FromText(MessageRole.User, "newer");
        var counter = new TrackingTokenCounter();

        // Original history message that triggers compaction
        counter.SetByText("original", 600);

        // Strategy returns two messages whose combined total (1100) still exceeds emergencyTrigger (900)
        counter.Set(olderMsg, 600);
        counter.Set(newerMsg, 500);

        var strategyResult = new CompactionResult([olderMsg, newerMsg], 600, 1100, 2, "TestStrategy");
        var strategy = new TrackingCompactionStrategy(strategyResult);
        var observer = new TrackingCompactionObserver();
        var engine = new ConversationContext(budget, counter, strategy, observer);

        engine.AddUserMessage("original");

        // Act
        var result = await engine.PrepareAsync();
        var prepared = result.Messages;

        // Assert — emergency truncation drops olderMsg; only newerMsg remains
        prepared.Should().ContainSingle().Which.Should().BeSameAs(newerMsg);

        observer.Events.Should().HaveCount(1);
        var evt = observer.Events[0];
        evt.Trigger.Should().Be(CompactionTrigger.Emergency);
        evt.Result.TokensBefore.Should().Be(600);
        evt.Result.TokensAfter.Should().Be(500);
        evt.Result.MessagesAffected.Should().Be(3);

        result.MessagesCompacted.Should().Be(3);
        result.MessagesDropped.Should().Be(1);
    }

    [Fact]
    public async Task PrepareAsync_WhenStrategyNotAppliedButEmergencyTruncationRuns_ReportsStrategyPassThroughAndEmergencyDropDetails()
    {
        // Arrange
        // Budget: compactionTrigger=500, emergencyTrigger=900
        var budget = new ContextBudget(1000, 0.5, 0.9);

        var counter = new TrackingTokenCounter();
        counter.SetByText("u1", 600);
        counter.SetByText("u2", 500);

        // Strategy returns input unchanged, but result still exceeds emergencyTrigger
        var strategy = new TrackingCompactionStrategy();
        var observer = new TrackingCompactionObserver();
        var engine = new ConversationContext(budget, counter, strategy, observer);

        engine.AddUserMessage("u1");
        engine.AddUserMessage("u2");

        // Act
        var result = await engine.PrepareAsync();

        // Assert — emergency truncation drops u1; observer is notified even though strategy did not apply
        observer.Events.Should().HaveCount(1);
        var evt = observer.Events[0];
        evt.Trigger.Should().Be(CompactionTrigger.Emergency);
        result.MessagesDropped.Should().Be(1);
        result.MessagesCompacted.Should().Be(1);
    }

    [Fact]
    public async Task PrepareAsync_WhenEmergencyTruncationRuns_SystemMessagesAreNeverDropped()
    {
        // Arrange
        // Budget: compactionTrigger=500, emergencyTrigger=900
        var budget = new ContextBudget(1000, 0.5, 0.9);

        var counter = new TrackingTokenCounter();
        counter.SetByText("sys", 800);
        counter.SetByText("older", 300);
        counter.SetByText("newer", 400);

        // Strategy passes through unchanged; combined total 1500 > emergencyTrigger(900)
        var strategy = new TrackingCompactionStrategy();
        var engine = new ConversationContext(budget, counter, strategy);

        engine.SetSystemPrompt("sys");
        engine.AddUserMessage("older");
        engine.AddUserMessage("newer");

        var sysMsg    = engine.History.First(m => m.Role == MessageRole.System);
        var olderMsg  = engine.History.First(m => m.Role == MessageRole.User);
        var newerMsg  = engine.History.Last(m => m.Role == MessageRole.User);

        // Act
        var result = await engine.PrepareAsync();
        var prepared = result.Messages;

        // Assert — system message is preserved; older user message is dropped; newer (floor) stays
        prepared.Should().HaveCount(2);
        prepared[0].Should().BeSameAs(sysMsg);
        prepared[1].Should().BeSameAs(newerMsg);
        prepared.Should().NotContain(olderMsg);
    }

    [Fact]
    public async Task PrepareAsync_WhenStrategyResultAlreadyBelowEmergencyThreshold_DoesNotApplyEmergencyTruncation()
    {
        // Arrange
        // Budget: compactionTrigger=500, emergencyTrigger=900
        var budget = new ContextBudget(1000, 0.5, 0.9);

        var compacted1 = ContextMessage.FromText(MessageRole.User, "c1");
        var compacted2 = ContextMessage.FromText(MessageRole.User, "c2");
        var counter = new TrackingTokenCounter();

        counter.SetByText("original", 600); // Triggers compaction
        counter.Set(compacted1, 400);
        counter.Set(compacted2, 300); // Combined 700 < emergencyTrigger(900)

        var strategyResult = new CompactionResult([compacted1, compacted2], 600, 700, 1, "TestStrategy");
        var strategy = new TrackingCompactionStrategy(strategyResult);
        var observer = new TrackingCompactionObserver();
        var engine = new ConversationContext(budget, counter, strategy, observer);

        engine.AddUserMessage("original");

        // Act
        var result = await engine.PrepareAsync();
        var prepared = result.Messages;

        // Assert — prepared list is exactly what the strategy returned, no messages removed
        prepared.Should().HaveCount(2);
        prepared[0].Should().BeSameAs(compacted1);
        prepared[1].Should().BeSameAs(compacted2);
        observer.Events.Should().HaveCount(1);
        observer.Events[0].Trigger.Should().Be(CompactionTrigger.Normal);
        result.MessagesDropped.Should().Be(0);
    }

    [Fact]
    public async Task PrepareAsync_WhenSummarizationStillExceedsEmergencyThreshold_DoesNotTruncateSummary()
    {
        // Arrange
        var budget = new ContextBudget(1000, 0.5, 0.9);

        var summary = ContextMessage.FromText(MessageRole.User, "summary") with { State = CompactionState.Summarized };
        var protectedTail = ContextMessage.FromText(MessageRole.User, "tail");
        var counter = new TrackingTokenCounter();

        counter.SetByText("original", 950);
        counter.Set(summary, 600);
        counter.Set(protectedTail, 500);

        var strategyResult = new CompactionResult(
            [summary, protectedTail],
            1100,
            950,
            3,
            "TestStrategy");

        var strategy = new TrackingCompactionStrategy(strategyResult);
        var observer = new TrackingCompactionObserver();
        var engine = new ConversationContext(budget, counter, strategy, observer);

        engine.AddUserMessage("original");

        // Act
        var result = await engine.PrepareAsync();

        // Assert
        result.Messages.Should().ContainInOrder(summary, protectedTail);
        result.MessagesDropped.Should().Be(0);
        result.MessagesCompacted.Should().Be(3);
        result.Outcome.Should().Be(PrepareOutcome.Degraded);
        result.TokensAfterCompaction.Should().Be(1100);
        result.DegradationReason.Should().NotBeNull();

        observer.Events.Should().HaveCount(1);
        observer.Events[0].Trigger.Should().Be(CompactionTrigger.Normal);
    }

    [Fact]
    public async Task PrepareAsync_WhenNewestUserFloorStillExceedsEmergencyThreshold_ReturnsNonEmptyOverBudgetFloor()
    {
        // Arrange
        var budget = new ContextBudget(1000, 0.5, 0.9);

        var counter = new TrackingTokenCounter();
        counter.SetByText("sys", 500);
        counter.SetByText("older-1", 200);
        counter.SetByText("older-2", 200);
        counter.SetByText("latest-user", 600);

        var strategy = new TrackingCompactionStrategy();
        var engine = new ConversationContext(budget, counter, strategy);

        engine.SetSystemPrompt("sys");
        engine.AddUserMessage("older-1");
        engine.AddUserMessage("older-2");
        engine.AddUserMessage("latest-user");

        var systemMessage = engine.History[0];
        var latestUser = engine.History[^1];

        // Act
        var result = await engine.PrepareAsync();
        var prepared = result.Messages;

        // Assert
        prepared.Should().HaveCount(2);
        prepared.Should().ContainInOrder(systemMessage, latestUser);
        prepared.Sum(message => message.TokenCount ?? 0).Should().Be(1100);
        result.Outcome.Should().Be(PrepareOutcome.Degraded);
        result.MessagesDropped.Should().Be(2);
    }

    [Fact]
    public async Task PrepareAsync_WhenNewestUserAndModelFloorStillExceedsEmergencyThreshold_PreservesNewestTailInOrder()
    {
        // Arrange
        var budget = new ContextBudget(1000, 0.5, 0.9);

        var counter = new TrackingTokenCounter();
        counter.SetByText("sys", 150);
        counter.SetByText("trigger", 1000);

        var newestModel = ContextMessage.FromText(MessageRole.Model, "newest-model");
        var olderUser = ContextMessage.FromText(MessageRole.User, "older-user");
        var newestUser = ContextMessage.FromText(MessageRole.User, "newest-user");

        counter.Set(olderUser, 250);
        counter.Set(newestUser, 700);
        counter.Set(newestModel, 550);

        var strategyResult = new CompactionResult(
            [olderUser, newestUser, newestModel],
            1350,
            1200,
            1,
            "TestStrategy");

        var strategy = new TrackingCompactionStrategy(strategyResult);
        var engine = new ConversationContext(budget, counter, strategy);

        engine.SetSystemPrompt("sys");
        engine.AddUserMessage("trigger");

        var systemMessage = engine.History[0];

        // Act
        var result = await engine.PrepareAsync();
        var prepared = result.Messages;

        // Assert
        prepared.Should().HaveCount(2);
        prepared.Should().ContainInOrder(systemMessage, olderUser);
        prepared.Should().NotContain(newestUser);
        prepared.Should().NotContain(newestModel);
        prepared.Sum(message => message.TokenCount ?? 0).Should().Be(400);
    }

    [Fact]
    public async Task PrepareAsync_WhenCompactionNotRequired_DoesNotInvokeObserver()
    {
        // Arrange
        var counter = new TrackingTokenCounter();
        var strategy = new TrackingCompactionStrategy();
        var observer = new TrackingCompactionObserver();
        var engine = new ConversationContext(ContextBudget.For(1_000), counter, strategy, observer);

        engine.AddUserMessage("hello");
        counter.Set(engine.History[0], 0);

        // Act
        var _ = await engine.PrepareAsync();

        // Assert
        observer.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task PrepareAsync_WhenCompactionOccursWithActiveAnchorCorrection_RecalibratesAnchorAgainstCompactedRawCountOnly()
    {
        // Arrange
        // Budget: compaction trigger = 500, emergency = 900.
        //
        // The bug: after compaction, _lastPreparedTotal is set to
        //   finalTokens + old_anchorCorrection  (wrong)
        // instead of:
        //   finalTokens                         (correct)
        //
        // This poisons the baseline for the next ApplyAnchor call, making the new
        // correction wrong by exactly old_anchorCorrection. When the pre-compaction
        // correction was large (+200), the poisoned baseline (-180 instead of +20)
        // prevents the subsequent PrepareAsync from detecting that the total has
        // crossed the compaction trigger, so the strategy is never called a second time.

        var budget = new ContextBudget(1000, 0.5, 0.9);
        var counter = new TrackingTokenCounter();
        var strategy = new TrackingCompactionStrategy();

        // Register token counts before adding messages so EnsureCounted caches them correctly.
        counter.SetByText("u1", 300);
        counter.SetByText("m1", 0);
        counter.SetByText("u2", 200);
        counter.SetByText("m2", 0);
        counter.SetByText("u3", 0);

        var engine = new ConversationContext(budget, counter, strategy);

        // Turn 1 — no compaction; establish a large pre-compaction anchor (+200).
        engine.AddUserMessage("u1");
        var _ = await engine.PrepareAsync();
        // total = 300, _lastPreparedTotal = 300

        engine.RecordModelResponse([new TextContent("m1")], providerInputTokens: 500);
        // _anchorCorrection = 500 - 300 = +200

        // Turn 2 — total = 300 + 0 + 200 + 200(correction) = 700 → compaction triggered.
        engine.AddUserMessage("u2");
        await engine.PrepareAsync();
        // CompactCalls = 1; strategy returns pass-through.
        // finalTokens = 500.
        // CORRECT: _lastPreparedTotal = 500,       _anchorCorrection = 0

        // True drift for the compacted messages is +20 (provider counts 520, raw estimate is 500).
        engine.RecordModelResponse([new TextContent("m2")], providerInputTokens: 520);
        // CORRECT: _anchorCorrection = 520 - 500 = +20

        // Turn 3 — rawSum(history) = 300 + 0 + 200 + 0 + 0 = 500.
        engine.AddUserMessage("u3");

        // Act
        await engine.PrepareAsync();

        // Assert
        // CORRECT: total = 500 + 20 = 520 ≥ 500 → compaction runs, CompactCalls = 2.
        strategy.CompactCalls.Should().Be(2);
    }

    [Fact]
    public async Task PrepareAsync_WhenEmergencyTruncationLeavesRawTotalOverBudgetButAdjustedTotalFits_DoesNotReportDegraded()
    {
        var budget = new ContextBudget(1_000, 0.5, 1.0);
        var counter = new TrackingTokenCounter();
        var dropCandidate = ContextMessage.FromText(MessageRole.User, "drop");
        var keepMiddle = ContextMessage.FromText(MessageRole.User, "keep-middle");
        var keepLatest = ContextMessage.FromText(MessageRole.User, "keep-latest");

        dropCandidate.Turn = 0;
        keepMiddle.Turn = 1;
        keepLatest.Turn = 2;

        counter.SetByText("seed", 400);
        counter.SetByText("reply", 0);
        counter.SetByText("trigger", 600);
        counter.Set(dropCandidate, 150);
        counter.Set(keepMiddle, 500);
        counter.Set(keepLatest, 550);

        var strategy = new TrackingCompactionStrategy(new CompactionResult(
            [dropCandidate, keepMiddle, keepLatest],
            1_000,
            1_200,
            1,
            "TestStrategy"));

        var engine = new ConversationContext(budget, counter, strategy);

        engine.AddUserMessage("seed");
        _ = await engine.PrepareAsync();

        engine.RecordModelResponse([new TextContent("reply")], providerInputTokens: 300);
        engine.AddUserMessage("trigger");

        var result = await engine.PrepareAsync();

        result.Outcome.Should().Be(PrepareOutcome.Compacted);
        result.TokensAfterCompaction.Should().Be(950);
        result.DegradationReason.Should().BeNull();
        result.MessagesDropped.Should().Be(1);
        result.Messages.Should().Equal(keepMiddle, keepLatest);
    }

    private static Task Sync(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task PrepareAsync_OutcomeReady_WhenBelowCompactionThreshold()
    {
        var counter = new TrackingTokenCounter();
        var strategy = new TrackingCompactionStrategy();
        var engine = new ConversationContext(ContextBudget.For(1_000), counter, strategy);

        counter.SetByText("hello", 100);
        engine.AddUserMessage("hello");

        var result = await engine.PrepareAsync();

        result.Outcome.Should().Be(PrepareOutcome.Ready);
        result.TokensBeforeCompaction.Should().Be(100);
        result.TokensAfterCompaction.Should().Be(100);
        result.MessagesCompacted.Should().Be(0);
        result.DegradationReason.Should().BeNull();
        result.MessagesDropped.Should().Be(0);
        result.Messages.Should().BeSameAs(engine.History);
    }

    [Fact]
    public async Task PrepareAsync_OutcomeCompacted_WhenStrategyReducesWithinBudget()
    {
        var compacted = ContextMessage.FromText(MessageRole.Model, "compacted");
        var counter = new TrackingTokenCounter();

        counter.SetByText("original", 900);
        counter.Set(compacted, 400);

        var strategy = new TrackingCompactionStrategy(new CompactionResult([compacted], 900, 400, 1, "TestStrategy"));
        var engine = new ConversationContext(ContextBudget.For(1_000), counter, strategy);

        engine.AddUserMessage("original");

        var result = await engine.PrepareAsync();

        result.Outcome.Should().Be(PrepareOutcome.Compacted);
        result.TokensBeforeCompaction.Should().Be(900);
        result.TokensAfterCompaction.Should().Be(400);
        result.MessagesCompacted.Should().Be(1);
        result.DegradationReason.Should().BeNull();
        result.MessagesDropped.Should().Be(0);
        result.Messages.Should().ContainSingle().Which.Should().BeSameAs(compacted);
    }

    [Fact]
    public async Task PrepareAsync_OutcomeDegraded_WhenCompactionStillExceedsBudget()
    {
        var compacted = ContextMessage.FromText(MessageRole.Model, "still-too-large");
        var counter = new TrackingTokenCounter();

        counter.SetByText("original", 900);
        counter.Set(compacted, 1100);

        var strategy = new TrackingCompactionStrategy(new CompactionResult([compacted], 900, 1100, 1, "TestStrategy"));
        var engine = new ConversationContext(ContextBudget.For(1_000), counter, strategy);

        engine.AddUserMessage("original");

        var result = await engine.PrepareAsync();

        result.Outcome.Should().Be(PrepareOutcome.Degraded);
        result.TokensBeforeCompaction.Should().Be(900);
        result.TokensAfterCompaction.Should().Be(1100);
        result.MessagesCompacted.Should().Be(1);
        result.DegradationReason.Should().NotBeNull();
        result.DegradationReason.Should().Contain("Compaction reduced content");
        result.MessagesDropped.Should().Be(0);
    }

    [Fact]
    public async Task PrepareAsync_OutcomeContextExhausted_WhenSingleMessageExceedsBudget()
    {
        var counter = new TrackingTokenCounter();
        var strategy = new TrackingCompactionStrategy();
        var engine = new ConversationContext(ContextBudget.For(1_000), counter, strategy);

        counter.SetByText("huge", 1500);
        engine.AddUserMessage("huge");

        var result = await engine.PrepareAsync();

        result.Outcome.Should().Be(PrepareOutcome.ContextExhausted);
        result.TokensBeforeCompaction.Should().Be(1500);
        result.TokensAfterCompaction.Should().Be(1500);
        result.MessagesCompacted.Should().Be(0);
        result.DegradationReason.Should().NotBeNull();
        result.DegradationReason.Should().Contain("exceeds the budget");
        result.MessagesDropped.Should().Be(0);
        result.Messages.Should().ContainSingle();
    }

    [Fact]
    public async Task PrepareAsync_OutcomeDegraded_WithPinnedMessages_WhenTruncationInsufficient()
    {
        var compacted = ContextMessage.FromText(MessageRole.Model, "compacted-still-large");
        var counter = new TrackingTokenCounter();

        counter.SetByText("sys", 100);
        counter.SetByText("original", 850);
        counter.Set(compacted, 1100);

        var strategy = new TrackingCompactionStrategy(new CompactionResult([compacted], 850, 1100, 1, "TestStrategy"));
        var engine = new ConversationContext(ContextBudget.For(1_000), counter, strategy);

        engine.SetSystemPrompt("sys");
        engine.AddUserMessage("original");

        var result = await engine.PrepareAsync();

        result.Outcome.Should().Be(PrepareOutcome.Degraded);
        result.TokensBeforeCompaction.Should().Be(950);
        result.MessagesCompacted.Should().BeGreaterThanOrEqualTo(1);
        result.DegradationReason.Should().NotBeNull();
    }

    [Fact]
    public async Task PrepareAsync_WithZeroTolerance_ReportsDegraded_WhenFinalTokensExceedBudget()
    {
        // Arrange — budget with explicit zero tolerance; compaction still leaves 1 token above max.
        var compacted = ContextMessage.FromText(MessageRole.Model, "still-over");
        var counter = new TrackingTokenCounter();

        counter.SetByText("original", 900);
        counter.Set(compacted, 1_001);

        var budget = new ContextBudget(maxTokens: 1_000, compactionThreshold: 0.80, overrunTolerance: 0.0);
        var strategy = new TrackingCompactionStrategy(new CompactionResult([compacted], 900, 1_001, 1, "TestStrategy"));
        var engine = new ConversationContext(budget, counter, strategy);

        engine.AddUserMessage("original");

        // Act
        var result = await engine.PrepareAsync();

        // Assert
        result.Outcome.Should().Be(PrepareOutcome.Degraded);
        result.TokensAfterCompaction.Should().Be(1_001);
        result.DegradationReason.Should().NotBeNull();
    }

    [Fact]
    public async Task PrepareAsync_WithPositiveTolerance_ReportsCompacted_WhenFinalTokensAreWithinTolerance()
    {
        // Arrange — tolerance of 10% (100 tokens on a 1000-token budget); compaction leaves 50 tokens above max, which is within tolerance.
        var compacted = ContextMessage.FromText(MessageRole.Model, "within-tolerance");
        var counter = new TrackingTokenCounter();

        counter.SetByText("original", 900);
        counter.Set(compacted, 1_050);

        var budget = new ContextBudget(maxTokens: 1_000, compactionThreshold: 0.80, overrunTolerance: 0.10);
        var strategy = new TrackingCompactionStrategy(new CompactionResult([compacted], 900, 1_050, 1, "TestStrategy"));
        var engine = new ConversationContext(budget, counter, strategy);

        engine.AddUserMessage("original");

        // Act
        var result = await engine.PrepareAsync();

        // Assert — 1050 <= 1000 + 100 → accepted as Compacted, not Degraded
        result.Outcome.Should().Be(PrepareOutcome.Compacted);
        result.TokensAfterCompaction.Should().Be(1_050);
        result.DegradationReason.Should().BeNull();
    }

    [Fact]
    public async Task PrepareAsync_WithPositiveTolerance_ReportsDegraded_WhenFinalTokensExceedBudgetPlusTolerance()
    {
        // Arrange — tolerance of 10% (100 tokens on a 1000-token budget); compaction leaves 101 tokens above max, which exceeds tolerance.
        var compacted = ContextMessage.FromText(MessageRole.Model, "outside-tolerance");
        var counter = new TrackingTokenCounter();

        counter.SetByText("original", 900);
        counter.Set(compacted, 1_101);

        var budget = new ContextBudget(maxTokens: 1_000, compactionThreshold: 0.80, overrunTolerance: 0.10);
        var strategy = new TrackingCompactionStrategy(new CompactionResult([compacted], 900, 1_101, 1, "TestStrategy"));
        var engine = new ConversationContext(budget, counter, strategy);

        engine.AddUserMessage("original");

        // Act
        var result = await engine.PrepareAsync();

        // Assert — 1101 > 1000 + 100 → still Degraded
        result.Outcome.Should().Be(PrepareOutcome.Degraded);
        result.TokensAfterCompaction.Should().Be(1_101);
        result.DegradationReason.Should().NotBeNull();
    }

    [Fact]
    public async Task PrepareAsync_WithPositiveTolerance_ReportsCompacted_WhenFinalTokensAreAtExactBudget()
    {
        // Arrange — regression: a result exactly at budget is always accepted regardless of tolerance.
        var compacted = ContextMessage.FromText(MessageRole.Model, "exact-budget");
        var counter = new TrackingTokenCounter();

        counter.SetByText("original", 900);
        counter.Set(compacted, 1_000);

        var budget = new ContextBudget(maxTokens: 1_000, compactionThreshold: 0.80, overrunTolerance: 0.10);
        var strategy = new TrackingCompactionStrategy(new CompactionResult([compacted], 900, 1_000, 1, "TestStrategy"));
        var engine = new ConversationContext(budget, counter, strategy);

        engine.AddUserMessage("original");

        // Act
        var result = await engine.PrepareAsync();

        // Assert — 1000 <= 1000 → Compacted (same as before tolerance was introduced)
        result.Outcome.Should().Be(PrepareOutcome.Compacted);
        result.TokensAfterCompaction.Should().Be(1_000);
        result.DegradationReason.Should().BeNull();
    }

    private static string GetText(ContextMessage message)
    {
        return Assert.IsType<TextContent>(Assert.Single(message.Segments)).Content;
    }

    private static void AssertText(ContextMessage message, string expected)
    {
        GetText(message).Should().Be(expected);
    }

    private sealed class TrackingCompactionStrategy : ICompactionStrategy
    {
        private readonly CompactionResult? _result;

        /// <summary>
        /// Initializes a compaction strategy test double that returns the supplied result.
        /// </summary>
        /// <param name="result">The compaction result to return from <see cref="CompactAsync"/>.</param>
        public TrackingCompactionStrategy(CompactionResult? result = null)
        {
            this._result = result;
        }

        /// <summary>
        /// Gets the number of times compaction has been requested.
        /// </summary>
        public int CompactCalls { get; private set; }

        /// <summary>
        /// Gets the last message sequence passed to <see cref="CompactAsync"/>.
        /// </summary>
        public IReadOnlyList<ContextMessage>? LastInput { get; private set; }

        /// <summary>
        /// Gets the last available-tokens value passed to <see cref="CompactAsync"/>.
        /// </summary>
        public int LastAvailableTokens { get; private set; }

        /// <summary>
        /// Records the compaction request and returns the configured result.
        /// </summary>
        /// <param name="messages">The messages selected for compaction.</param>
        /// <param name="availableTokens">The token budget available to the compaction operation.</param>
        /// <param name="tokenCounter">The token counter associated with the request.</param>
        /// <param name="cancellationToken">A token used to cancel the operation.</param>
        /// <returns>A task containing either the configured result or a pass-through compaction result.</returns>
        public Task<CompactionResult> CompactAsync(IReadOnlyList<ContextMessage> messages, int availableTokens, ITokenCounter tokenCounter, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.CompactCalls++;
            this.LastInput = messages;
            this.LastAvailableTokens = availableTokens;

            return Task.FromResult(this._result ?? new CompactionResult(messages, 0, 0, 0, nameof(TrackingCompactionStrategy)));
        }
    }

    private sealed class TrackingTokenCounter : ITokenCounter
    {
        private readonly Dictionary<ContextMessage, int> _counts = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<ContextMessage, int> _calls = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<string, int> _countsByText = new();

        /// <summary>
        /// Registers a token count for a specific message instance.
        /// </summary>
        /// <param name="contextMessage">The message whose token count should be returned.</param>
        /// <param name="count">The token count to return.</param>
        public void Set(ContextMessage contextMessage, int count)
        {
            this._counts[contextMessage] = count;
        }

        /// <summary>
        /// Registers a token count returned whenever a message's first text segment matches <paramref name="text"/>.
        /// </summary>
        /// <param name="text">The first text segment used to look up a token count.</param>
        /// <param name="count">The token count to return for matching messages.</param>
        public void SetByText(string text, int count)
        {
            this._countsByText[text] = count;
        }

        /// <summary>
        /// Gets the number of times a specific message has been counted.
        /// </summary>
        /// <param name="contextMessage">The message whose count invocations should be returned.</param>
        /// <returns>The number of count invocations recorded for the message.</returns>
        public int GetCountCalls(ContextMessage contextMessage)
        {
            return this._calls.GetValueOrDefault(contextMessage, 0);
        }

        /// <summary>
        /// Returns the configured token count for a single message and records the invocation.
        /// </summary>
        /// <param name="contextMessage">The message to count.</param>
        /// <returns>The configured token count for the message.</returns>
        public int Count(ContextMessage contextMessage)
        {
            this._calls[contextMessage] = this.GetCountCalls(contextMessage) + 1;

            if (this._counts.TryGetValue(contextMessage, out var cached))
                return cached;

            var firstText = contextMessage.Segments.OfType<TextContent>().FirstOrDefault()?.Content;
            if (firstText != null && this._countsByText.TryGetValue(firstText, out var byText))
                return byText;

            return 0;
        }

        /// <summary>
        /// Returns the total configured token count for a sequence of messages.
        /// </summary>
        /// <param name="messages">The messages to count.</param>
        /// <returns>The total token count across the sequence.</returns>
        public int Count(IEnumerable<ContextMessage> messages)
        {
            ArgumentNullException.ThrowIfNull(messages);

            return messages.Sum(this.Count);
        }
    }

    private sealed class TrackingCompactionObserver : ICompactionObserver
    {
        public List<CompactionEvent> Events { get; } = [];

        public void OnCompaction(CompactionEvent compactionEvent) => this.Events.Add(compactionEvent);
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<ContextMessage>
    {
        /// <summary>
        /// Gets the shared comparer instance.
        /// </summary>
        public static readonly ReferenceEqualityComparer Instance = new();

        /// <summary>
        /// Determines whether two message references are the same instance.
        /// </summary>
        /// <param name="x">The first message reference.</param>
        /// <param name="y">The second message reference.</param>
        /// <returns><see langword="true"/> when both references point to the same instance; otherwise, <see langword="false"/>.</returns>
        public bool Equals(ContextMessage? x, ContextMessage? y)
        {
            return ReferenceEquals(x, y);
        }

        /// <summary>
        /// Returns a hash code based on object identity.
        /// </summary>
        /// <param name="obj">The message reference to hash.</param>
        /// <returns>A hash code derived from the object identity.</returns>
        public int GetHashCode(ContextMessage obj)
        {
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
