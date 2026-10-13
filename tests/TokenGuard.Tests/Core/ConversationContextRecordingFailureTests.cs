using FluentAssertions;
using TokenGuard.Core;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Configuration;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Exceptions;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;
using TokenGuard.Tests.Diagnostics;

namespace TokenGuard.Tests.Core;

public sealed class ConversationContextRecordingFailureTests
{
    private const string Uncountable = "uncountable";

    [Fact]
    public async Task PrepareAsync_WhenAToolResultEndsWithHalfOfASurrogatePair_ReturnsTheHistory()
    {
        // Arrange
        using var context = new ConversationContextFactory(ConversationConfigBuilder.Default()).Create();
        context.AddUserMessage("Read the file.");
        context.RecordModelResponse([new ToolUseContent("call_1", "read_file", """{"path":"notes.txt"}""")]);
        context.RecordToolResult("call_1", "read_file", "output cut at a character limit \uD83D");

        // Act
        var result = await context.PrepareAsync();

        // Assert
        result.Outcome.Should().Be(PrepareOutcome.Ready);
        result.Messages.Should().HaveCount(3);
    }

    [Fact]
    public async Task AddUserMessage_WhenCountingTheMessageThrows_LeavesTheHistoryAsItWas()
    {
        // Arrange
        using var context = CreateContext();
        context.AddUserMessage("first");

        // Act
        var act = () => context.AddUserMessage(Uncountable);

        // Assert
        act.Should().Throw<InvalidOperationException>();
        context.History.Select(Text).Should().Equal("first");
        (await context.PrepareAsync()).Messages.Select(Text).Should().Equal("first");
    }

    [Fact]
    public async Task SetSystemPrompt_WhenCountingTheNewPromptThrows_KeepsThePreviousPromptAndItsTokens()
    {
        // Arrange
        using var context = CreateContext();
        context.SetSystemPrompt("system prompt");
        context.AddUserMessage("first");
        var before = await context.PrepareAsync();

        // Act
        var act = () => context.SetSystemPrompt(Uncountable);

        // Assert
        act.Should().Throw<InvalidOperationException>();
        var after = await context.PrepareAsync();
        after.Messages.Select(Text).Should().Equal("system prompt", "first");
        after.TokensAfterCompaction.Should().Be(before.TokensAfterCompaction);
    }

    [Fact]
    public async Task SetSystemPrompt_WhenCountingTheNewPromptThrows_KeepsThePinnedTokenTotal()
    {
        // Arrange
        using var context = new ConversationContext(new ContextBudget(20), new FailingTokenCounter(), StubCompactionStrategy.Unchanged());
        context.SetSystemPrompt("thirteen char");
        try
        {
            context.SetSystemPrompt(Uncountable);
        }
        catch (InvalidOperationException)
        {
            // The failed replacement is the arranged state.
        }

        context.AddPinnedMessage(MessageRole.User, "ten chars.");

        // Act
        var act = () => context.PrepareAsync();

        // Assert
        (await act.Should().ThrowAsync<PinnedTokenBudgetExceededException>()).Which.PinnedTokenTotal.Should().Be(23);
    }

    private static ConversationContext CreateContext() =>
        new(ContextBudget.For(1_000), new FailingTokenCounter(), StubCompactionStrategy.Unchanged());

    private static string Text(ContextMessage message) => message.Segments[0].Content;

    /// <summary>
    ///     Represents a token counter that charges one token for each character and throws for one marker text.
    /// </summary>
    private sealed class FailingTokenCounter : ITokenCounter
    {
        /// <inheritdoc />
        public int Count(ContextMessage contextMessage)
        {
            if (Text(contextMessage) == Uncountable)
                throw new InvalidOperationException("The message cannot be counted.");

            return contextMessage.Segments.Sum(segment => segment.Content.Length);
        }

        /// <inheritdoc />
        public int Count(IEnumerable<ContextMessage> messages) => messages.Sum(this.Count);
    }
}
