using FluentAssertions;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Contexts;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;

namespace TokenGuard.Tests.Core;

public sealed class AgentTurnTests
{
    // ── AgentTurn unit tests ──────────────────────────────────────────────────

    [Fact]
    public void AgentTurn_Constructor_StoresInitialMessage()
    {
        // Arrange
        var message = ContextMessage.FromText(MessageRole.User, "hello");

        // Act
        var turn = new AgentTurn(message);

        // Assert
        turn.Messages.Should().ContainSingle().Which.Should().BeSameAs(message);
        turn.TokenTotal.Should().BeNull();
        turn.HasPinnedMessages.Should().BeFalse();
    }

    [Fact]
    public void AgentTurn_Constructor_WithPinnedMessage_SetsHasPinnedMessagesTrue()
    {
        // Arrange
        var message = ContextMessage.FromText(MessageRole.User, "durable") with { IsPinned = true };

        // Act
        var turn = new AgentTurn(message);

        // Assert
        turn.HasPinnedMessages.Should().BeTrue();
    }

    [Fact]
    public void AgentTurn_Append_AddsMessageAndInvalidatesTokenTotal()
    {
        // Arrange
        var first = ContextMessage.FromText(MessageRole.Model, "model");
        var second = ContextMessage.FromContent(MessageRole.Tool, new ToolResultContent("id1", "tool", "result"));
        var turn = new AgentTurn(first);
        turn.TokenTotal = 42;

        // Act
        turn.Append(second);

        // Assert
        turn.Messages.Should().HaveCount(2);
        turn.Messages[0].Should().BeSameAs(first);
        turn.Messages[1].Should().BeSameAs(second);
        turn.TokenTotal.Should().BeNull();
    }

    [Fact]
    public void AgentTurn_Append_UnpinnedMessage_DoesNotSetHasPinnedMessages()
    {
        // Arrange
        var first = ContextMessage.FromText(MessageRole.Model, "model");
        var tool = ContextMessage.FromContent(MessageRole.Tool, new ToolResultContent("id", "t", "out"));
        var turn = new AgentTurn(first);

        // Act
        turn.Append(tool);

        // Assert
        turn.HasPinnedMessages.Should().BeFalse();
    }

    [Fact]
    public void AgentTurn_ReplaceOnly_SwapsMessageAndInvalidatesTokenTotal()
    {
        // Arrange
        var original = ContextMessage.FromText(MessageRole.System, "old");
        var replacement = ContextMessage.FromText(MessageRole.System, "new");
        var turn = new AgentTurn(original);
        turn.TokenTotal = 10;

        // Act
        turn.ReplaceOnly(replacement);

        // Assert
        turn.Messages.Should().ContainSingle().Which.Should().BeSameAs(replacement);
        turn.TokenTotal.Should().BeNull();
    }

    [Fact]
    public void AgentTurn_TokenTotal_CanBeSetAndRead()
    {
        // Arrange
        var turn = new AgentTurn(ContextMessage.FromText(MessageRole.User, "hello"));

        // Act
        turn.TokenTotal = 99;

        // Assert
        turn.TokenTotal.Should().Be(99);
    }

    // ── Turn-grouping behavior tests via ConversationContext ─────────────────

    [Fact]
    public void RecordModelResponse_ProseOnly_CreatesSingleMessageTurn()
    {
        // Arrange
        var context = BuildContext();

        // Act
        context.RecordModelResponse([new TextContent("response")]);

        // Assert
        context.Turns.Should().ContainSingle();
        context.Turns[0].Messages.Should().ContainSingle();
        context.Turns[0].Messages[0].Role.Should().Be(MessageRole.Model);
    }

    [Fact]
    public void RecordToolResult_ImmediatelyAfterModelResponse_AppendsToSameTurn()
    {
        // Arrange
        var context = BuildContext();
        context.RecordModelResponse([new ToolUseContent("id1", "search", "{}")]);

        // Act
        context.RecordToolResult("id1", "search", "results");

        // Assert
        context.Turns.Should().ContainSingle();
        context.Turns[0].Messages.Should().HaveCount(2);
        context.Turns[0].Messages[0].Role.Should().Be(MessageRole.Model);
        context.Turns[0].Messages[1].Role.Should().Be(MessageRole.Tool);
    }

    [Fact]
    public void RecordToolResult_MultipleAfterModelResponse_AllInSameTurn()
    {
        // Arrange
        var context = BuildContext();
        context.RecordModelResponse([new ToolUseContent("id1", "tool-a", "{}"), new ToolUseContent("id2", "tool-b", "{}")]);

        // Act
        context.RecordToolResult("id1", "tool-a", "a-result");
        context.RecordToolResult("id2", "tool-b", "b-result");

        // Assert
        context.Turns.Should().ContainSingle();
        context.Turns[0].Messages.Should().HaveCount(3);
        context.Turns[0].Messages[1].Role.Should().Be(MessageRole.Tool);
        context.Turns[0].Messages[2].Role.Should().Be(MessageRole.Tool);
    }

    [Fact]
    public void RecordModelResponse_MultipleConsecutive_EachInSeparateTurn()
    {
        // Arrange
        var context = BuildContext();

        // Act
        context.RecordModelResponse([new TextContent("first")]);
        context.RecordModelResponse([new TextContent("second")]);
        context.RecordModelResponse([new TextContent("third")]);

        // Assert
        context.Turns.Should().HaveCount(3);
        context.Turns.Should().AllSatisfy(t => t.Messages.Should().ContainSingle());
        context.Turns.Should().AllSatisfy(t => t.Messages[0].Role.Should().Be(MessageRole.Model));
    }

    [Fact]
    public void AddUserMessage_AfterToolResults_StartsNewTurn()
    {
        // Arrange
        var context = BuildContext();
        context.RecordModelResponse([new ToolUseContent("id1", "tool", "{}")]);
        context.RecordToolResult("id1", "tool", "result");

        // Act
        context.AddUserMessage("follow-up");

        // Assert
        context.Turns.Should().HaveCount(2);
        context.Turns[0].Messages.Should().HaveCount(2);
        context.Turns[1].Messages.Should().ContainSingle();
        context.Turns[1].Messages[0].Role.Should().Be(MessageRole.User);
    }

    [Fact]
    public void AddUserMessage_InterleavedWithModelResponses_EachOccupiesOwnTurn()
    {
        // Arrange
        var context = BuildContext();

        // Act
        context.AddUserMessage("u1");
        context.RecordModelResponse([new TextContent("m1")]);
        context.AddUserMessage("u2");
        context.RecordModelResponse([new TextContent("m2")]);

        // Assert
        context.Turns.Should().HaveCount(4);
        context.Turns[0].Messages[0].Role.Should().Be(MessageRole.User);
        context.Turns[1].Messages[0].Role.Should().Be(MessageRole.Model);
        context.Turns[2].Messages[0].Role.Should().Be(MessageRole.User);
        context.Turns[3].Messages[0].Role.Should().Be(MessageRole.Model);
    }

    [Fact]
    public void AddPinnedMessage_AlwaysOccupiesOwnTurnWithPinnedFlag()
    {
        // Arrange
        var context = BuildContext();
        context.AddUserMessage("u1");
        context.AddPinnedMessage(MessageRole.User, "pinned");

        // Act
        context.AddUserMessage("u2");

        // Assert
        context.Turns.Should().HaveCount(3);
        context.Turns[1].HasPinnedMessages.Should().BeTrue();
        context.Turns[0].HasPinnedMessages.Should().BeFalse();
        context.Turns[2].HasPinnedMessages.Should().BeFalse();
    }

    [Fact]
    public void SetSystemPrompt_WhenNoSystemExists_InsertsOwnTurnAtFront()
    {
        // Arrange
        var context = BuildContext();
        context.AddUserMessage("u1");

        // Act
        context.SetSystemPrompt("sys");

        // Assert
        context.Turns.Should().HaveCount(2);
        context.Turns[0].Messages[0].Role.Should().Be(MessageRole.System);
        context.Turns[1].Messages[0].Role.Should().Be(MessageRole.User);
    }

    [Fact]
    public void SetSystemPrompt_WhenReplacingExisting_UpdatesInPlaceWithoutAddingTurn()
    {
        // Arrange
        var context = BuildContext();
        context.SetSystemPrompt("original");
        context.AddUserMessage("u1");

        // Act
        context.SetSystemPrompt("updated");

        // Assert
        context.Turns.Should().HaveCount(2);
        context.Turns[0].Messages.Should().ContainSingle();
        AssertText(context.Turns[0].Messages[0], "updated");
    }

    [Fact]
    public void PinnedMessages_ScatteredAcrossHistory_EachInOwnTurnWithPinnedFlag()
    {
        // Arrange
        var context = BuildContext();

        // Act
        context.AddPinnedMessage(MessageRole.System, "pin-a");
        context.AddUserMessage("u1");
        context.AddPinnedMessage(MessageRole.User, "pin-b");
        context.RecordModelResponse([new TextContent("m1")]);
        context.AddPinnedMessage(MessageRole.User, "pin-c");

        // Assert
        context.Turns.Should().HaveCount(5);
        context.Turns[0].HasPinnedMessages.Should().BeTrue();
        context.Turns[1].HasPinnedMessages.Should().BeFalse();
        context.Turns[2].HasPinnedMessages.Should().BeTrue();
        context.Turns[3].HasPinnedMessages.Should().BeFalse();
        context.Turns[4].HasPinnedMessages.Should().BeTrue();
    }

    [Fact]
    public void History_AfterMixedRecording_ReflectsCorrectFlatOrder()
    {
        // Arrange
        var context = BuildContext();

        // Act
        context.SetSystemPrompt("sys");
        context.AddUserMessage("u1");
        context.RecordModelResponse([new ToolUseContent("id1", "tool", "{}")]);
        context.RecordToolResult("id1", "tool", "result");
        context.AddUserMessage("u2");

        // Assert
        context.History.Should().HaveCount(5);
        context.History[0].Role.Should().Be(MessageRole.System);
        context.History[1].Role.Should().Be(MessageRole.User);
        context.History[2].Role.Should().Be(MessageRole.Model);
        context.History[3].Role.Should().Be(MessageRole.Tool);
        context.History[4].Role.Should().Be(MessageRole.User);
    }

    [Fact]
    public void History_TurnMessagesAndFlatHistory_ContainSameMessageReferences()
    {
        // Arrange
        var context = BuildContext();
        context.RecordModelResponse([new ToolUseContent("id1", "tool", "{}")]);
        context.RecordToolResult("id1", "tool", "result");

        // Act
        var fromTurn = context.Turns[0].Messages;
        var fromHistory = context.History;

        // Assert — same object references, not copies
        fromHistory[0].Should().BeSameAs(fromTurn[0]);
        fromHistory[1].Should().BeSameAs(fromTurn[1]);
    }

    private static ConversationContext BuildContext() =>
        new(ContextBudget.For(100_000), new NoOpTokenCounter(), new NoOpCompactionStrategy());

    private static string GetText(ContextMessage message) =>
        Assert.IsType<TextContent>(Assert.Single(message.Segments)).Content;

    private static void AssertText(ContextMessage message, string expected) =>
        GetText(message).Should().Be(expected);

    private sealed class NoOpTokenCounter : ITokenCounter
    {
        public int Count(ContextMessage message) => 0;

        public int Count(IEnumerable<ContextMessage> messages) => 0;
    }

    private sealed class NoOpCompactionStrategy : ICompactionStrategy
    {
        public Task<CompactionResult> CompactAsync(
            IReadOnlyList<ContextMessage> messages,
            ContextBudget budget,
            ITokenCounter tokenCounter,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CompactionResult(messages, 0, 0, 0, nameof(NoOpCompactionStrategy), false));
    }
}
