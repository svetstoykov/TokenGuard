using FluentAssertions;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Contexts;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;

namespace TokenGuard.Tests.Core;

public sealed class AgentTurnGroupingTests
{
    [Fact]
    public void History_ProseOnlyModelTurn_FlattensCorrectly()
    {
        var engine = CreateEngine();

        engine.AddUserMessage("question");
        engine.RecordModelResponse([new TextContent("answer")]);

        engine.History.Should().HaveCount(2);
        engine.History[0].Role.Should().Be(MessageRole.User);
        engine.History[1].Role.Should().Be(MessageRole.Model);
    }

    [Fact]
    public void History_ModelPlusMultipleToolTurn_FlattensCorrectly()
    {
        var engine = CreateEngine();

        engine.AddUserMessage("run tools");
        engine.RecordModelResponse([new ToolUseContent("call_1", "tool_a", "{}")]);
        engine.RecordToolResult("call_1", "tool_a", "result_a");
        engine.RecordToolResult("call_2", "tool_b", "result_b");

        engine.History.Should().HaveCount(4);
        engine.History[0].Role.Should().Be(MessageRole.User);
        engine.History[1].Role.Should().Be(MessageRole.Model);
        engine.History[2].Role.Should().Be(MessageRole.Tool);
        engine.History[3].Role.Should().Be(MessageRole.Tool);
    }

    [Fact]
    public void History_MultipleConsecutiveModelTurns_EachInOwnTurn()
    {
        var engine = CreateEngine();

        engine.AddUserMessage("q1");
        engine.RecordModelResponse([new TextContent("a1")]);
        engine.AddUserMessage("q2");
        engine.RecordModelResponse([new TextContent("a2")]);
        engine.AddUserMessage("q3");
        engine.RecordModelResponse([new TextContent("a3")]);

        engine.History.Should().HaveCount(6);
        engine.History.Select(m => m.Role).Should().Equal(
            MessageRole.User, MessageRole.Model,
            MessageRole.User, MessageRole.Model,
            MessageRole.User, MessageRole.Model);
    }

    [Fact]
    public void History_UserTurnsInterleavedWithModelTurns_UserAlwaysStartsOwnTurn()
    {
        var engine = CreateEngine();

        engine.AddUserMessage("q1");
        engine.RecordModelResponse([new ToolUseContent("call_1", "tool", "{}")]);
        engine.RecordToolResult("call_1", "tool", "result");
        engine.AddUserMessage("q2-interrupt");
        engine.RecordModelResponse([new TextContent("a2")]);

        engine.History.Should().HaveCount(5);
        engine.History.Select(m => m.Role).Should().Equal(
            MessageRole.User,
            MessageRole.Model,
            MessageRole.Tool,
            MessageRole.User,
            MessageRole.Model);
    }

    [Fact]
    public void History_PinnedMessagesScatteredAcrossHistory_FlattenPreservesOrder()
    {
        var engine = CreateEngine();

        engine.SetSystemPrompt("system");
        engine.AddPinnedMessage(MessageRole.User, "durable-instruction");
        engine.AddUserMessage("normal user");
        engine.RecordModelResponse([new TextContent("reply")]);
        engine.AddPinnedMessage(MessageRole.Model, "pinned-reply");

        engine.History.Should().HaveCount(5);
        engine.History[0].Role.Should().Be(MessageRole.System);
        engine.History[0].IsPinned.Should().BeTrue();
        engine.History[1].Role.Should().Be(MessageRole.User);
        engine.History[1].IsPinned.Should().BeTrue();
        engine.History[2].Role.Should().Be(MessageRole.User);
        engine.History[2].IsPinned.Should().BeFalse();
        engine.History[3].Role.Should().Be(MessageRole.Model);
        engine.History[3].IsPinned.Should().BeFalse();
        engine.History[4].Role.Should().Be(MessageRole.Model);
        engine.History[4].IsPinned.Should().BeTrue();
    }

    [Fact]
    public void History_ToolResultWithoutPrecedingModel_StartsOwnTurn()
    {
        var engine = CreateEngine();

        engine.RecordToolResult("call_1", "tool", "orphan-result");

        engine.History.Should().HaveCount(1);
        engine.History[0].Role.Should().Be(MessageRole.Tool);
    }

    [Fact]
    public void History_SystemPromptAfterOtherMessages_InsertedAtFront()
    {
        var engine = CreateEngine();

        engine.AddUserMessage("user first");
        engine.SetSystemPrompt("system added later");

        engine.History.Should().HaveCount(2);
        engine.History[0].Role.Should().Be(MessageRole.System);
        engine.History[1].Role.Should().Be(MessageRole.User);
    }

    [Fact]
    public void History_LiveView_ReflectsNewMessagesImmediately()
    {
        var engine = CreateEngine();

        engine.AddUserMessage("msg1");
        var firstView = engine.History;
        firstView.Should().HaveCount(1);

        engine.AddUserMessage("msg2");
        var secondView = engine.History;
        secondView.Should().HaveCount(2);

        firstView.Should().BeSameAs(secondView, "History should return the same list reference");
    }

    [Fact]
    public async Task PrepareAsync_BelowThreshold_ReturnsSameReferenceAsHistory()
    {
        var engine = CreateEngine();
        engine.AddUserMessage("hello");

        var prepared = await engine.PrepareAsync();
        prepared.Should().BeSameAs(engine.History);
    }

    [Fact]
    public void AgentTurn_AddMessage_UpdatesHasPinnedFlag()
    {
        var turn = new AgentTurn();
        turn.HasPinnedMessages.Should().BeFalse();

        var unpinned = ContextMessage.FromText(MessageRole.User, "unpinned");
        turn.AddMessage(unpinned);
        turn.HasPinnedMessages.Should().BeFalse();

        var pinned = ContextMessage.FromText(MessageRole.User, "pinned") with { IsPinned = true };
        turn.AddMessage(pinned);
        turn.HasPinnedMessages.Should().BeTrue();
    }

    [Fact]
    public void AgentTurn_TokenCount_StartsNull()
    {
        var turn = new AgentTurn();
        turn.TokenCount.Should().BeNull();
    }

    [Fact]
    public void AgentTurn_Messages_ReturnsMutableList()
    {
        var turn = new AgentTurn();
        var msg = ContextMessage.FromText(MessageRole.User, "test");
        turn.AddMessage(msg);

        turn.Messages.Should().ContainSingle().Which.Should().BeSameAs(msg);
        turn.Messages.Should().BeAssignableTo<IList<ContextMessage>>();
    }

    private static ConversationContext CreateEngine()
    {
        return new ConversationContext(
            ContextBudget.For(10_000),
            new NoOpTokenCounter(),
            new PassThroughCompactionStrategy());
    }

    private sealed class NoOpTokenCounter : ITokenCounter
    {
        public int Count(ContextMessage contextMessage) => contextMessage.TokenCount ?? 0;
        public int Count(IEnumerable<ContextMessage> messages) => messages.Sum(m => m.TokenCount ?? 0);
    }

    private sealed class PassThroughCompactionStrategy : ICompactionStrategy
    {
        public Task<CompactionResult> CompactAsync(
            IReadOnlyList<ContextMessage> messages,
            ContextBudget budget,
            ITokenCounter tokenCounter,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new CompactionResult(
                messages,
                tokenCounter.Count(messages),
                tokenCounter.Count(messages),
                0,
                nameof(PassThroughCompactionStrategy),
                WasApplied: false));
        }
    }
}
