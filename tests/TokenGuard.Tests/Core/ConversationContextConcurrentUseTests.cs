using System.Runtime.ExceptionServices;
using FluentAssertions;
using TokenGuard.Core;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Exceptions;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;
using TokenGuard.Tests.Diagnostics;

namespace TokenGuard.Tests.Core;

public sealed class ConversationContextConcurrentUseTests
{
    private const string SetSystemPrompt = "SetSystemPrompt";
    private const string AddPinnedText = "AddPinnedMessage(text)";
    private const string AddPinnedSegments = "AddPinnedMessage(segments)";
    private const string AddUserMessage = "AddUserMessage";
    private const string RecordModelResponse = "RecordModelResponse";
    private const string RecordToolResult = "RecordToolResult";

    private const string OverlapMessage =
        "A second operation was started on this conversation context before a previous operation completed. "
        + "A context supports one caller at a time. "
        + "Await PrepareAsync before recording, and record the results of parallel tool calls one after another.";

    private static readonly string[] RecordingMembers =
        [SetSystemPrompt, AddPinnedText, AddPinnedSegments, AddUserMessage, RecordModelResponse, RecordToolResult];

    [Theory]
    [InlineData(SetSystemPrompt)]
    [InlineData(AddPinnedText)]
    [InlineData(AddPinnedSegments)]
    [InlineData(AddUserMessage)]
    [InlineData(RecordModelResponse)]
    [InlineData(RecordToolResult)]
    public async Task RecordingCall_WhenAPrepareIsStillRunning_ThrowsInvalidOperationException(string member)
    {
        // Arrange
        var strategy = new HeldStrategy();
        using var context = CreateContext(strategy);
        context.AddUserMessage(Text("opening", 600));
        var pending = context.PrepareAsync();

        // Act
        var act = () => Record(context, member);

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage(OverlapMessage);

        strategy.Release();
        await pending;
    }

    [Fact]
    public async Task PrepareAsync_WhenAPrepareIsStillRunning_ReportsInvalidOperationExceptionThroughTheReturnedTask()
    {
        // Arrange
        var strategy = new HeldStrategy();
        using var context = CreateContext(strategy);
        context.AddUserMessage(Text("opening", 600));
        var pending = context.PrepareAsync();

        // Act
        var second = context.PrepareAsync();

        // Assert
        second.IsFaulted.Should().BeTrue("an overlapping prepare fails before it awaits anything");
        second.Exception!.InnerException.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Be(OverlapMessage);

        strategy.Release();
        await pending;
    }

    [Fact]
    public async Task RecordingCall_WhenAnEarlierOverlappingCallWasRejected_IsStillRejectedWhileThePrepareRuns()
    {
        // Arrange
        var strategy = new HeldStrategy();
        using var context = CreateContext(strategy);
        context.AddUserMessage(Text("opening", 600));
        var pending = context.PrepareAsync();
        context.Invoking(c => c.AddUserMessage("first")).Should().Throw<InvalidOperationException>();

        // Act
        var act = () => context.AddUserMessage("second");

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage(OverlapMessage);

        strategy.Release();
        await pending;
    }

    [Fact]
    public async Task PrepareAsync_WhenOverlappingCallsWereRejected_CompletesWithTheHistoryRecordedBeforeIt()
    {
        // Arrange
        var strategy = new HeldStrategy();
        using var context = CreateContext(strategy);
        context.AddUserMessage(Text("opening", 600));
        var pending = context.PrepareAsync();
        foreach (var member in RecordingMembers)
            context.Invoking(c => Record(c, member)).Should().Throw<InvalidOperationException>();

        // Act
        strategy.Release();
        var result = await pending;

        // Assert
        result.Messages.Select(Describe).Should().Equal("User:opening");
        context.History.Select(Describe).Should().Equal("User:opening");
    }

    [Fact]
    public async Task RecordingCalls_AfterTheHeldPrepareCompletes_SucceedAndAreTheOnlyMessagesAdded()
    {
        // Arrange
        var strategy = new HeldStrategy();
        using var context = CreateContext(strategy);
        context.AddUserMessage(Text("opening", 600));
        var pending = context.PrepareAsync();
        foreach (var member in RecordingMembers)
            context.Invoking(c => Record(c, member)).Should().Throw<InvalidOperationException>();
        strategy.Release();
        await pending;

        // Act
        var act = () =>
        {
            foreach (var member in RecordingMembers)
                Record(context, member);
        };

        // Assert
        act.Should().NotThrow();
        context.History.Select(Describe).Should().Equal(
            "System:system", "User:opening", "User:pinned-text", "User:pinned-segments", "User:user", "Model:call_1", "Tool:call_1");
    }

    [Fact]
    public async Task PrepareAsync_AfterTheHeldPrepareCompletes_Succeeds()
    {
        // Arrange
        var strategy = new HeldStrategy();
        using var context = CreateContext(strategy);
        context.AddUserMessage(Text("opening", 600));
        var pending = context.PrepareAsync();
        var rejected = context.PrepareAsync();
        rejected.IsFaulted.Should().BeTrue("the overlapping prepare is rejected without waiting");
        rejected.Exception!.InnerException.Should().BeOfType<InvalidOperationException>();
        strategy.Release();
        await pending;

        // Act
        var result = await context.PrepareAsync();

        // Assert
        result.Messages.Select(Describe).Should().Equal("User:opening");
    }

    [Theory]
    [InlineData(SetSystemPrompt)]
    [InlineData(AddPinnedText)]
    [InlineData(AddPinnedSegments)]
    [InlineData(AddUserMessage)]
    [InlineData(RecordModelResponse)]
    [InlineData(RecordToolResult)]
    public void RecordingCall_AfterACallThrewForAnInvalidArgument_Succeeds(string member)
    {
        // Arrange
        using var context = CreateContext(StubCompactionStrategy.Unchanged());
        context.Invoking(c => RecordWithInvalidArgument(c, member)).Should().Throw<ArgumentException>();

        // Act
        var act = () => context.AddUserMessage("next");

        // Assert
        act.Should().NotThrow();
        context.History.Select(Describe).Should().Equal("User:next");
    }

    [Fact]
    public async Task RecordingCall_AfterAPrepareWasCancelled_Succeeds()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        using var context = CreateContext(new HeldStrategy());
        context.AddUserMessage(Text("opening", 600));
        var pending = context.PrepareAsync(cancellation.Token);
        await cancellation.CancelAsync();
        await pending.Awaiting(task => task).Should().ThrowAsync<OperationCanceledException>();

        // Act
        var act = () => context.AddUserMessage("next");

        // Assert
        act.Should().NotThrow();
        context.History.Select(Describe).Should().Equal("User:opening", "User:next");
    }

    [Fact]
    public async Task RecordingCall_AfterTheStrategyThrewDuringAPrepare_Succeeds()
    {
        // Arrange
        var strategy = new HeldStrategy();
        using var context = CreateContext(strategy);
        context.AddUserMessage(Text("opening", 600));
        var pending = context.PrepareAsync();
        strategy.Fail(new TimeoutException("strategy failed"));
        await pending.Awaiting(task => task).Should().ThrowAsync<TimeoutException>();

        // Act
        var act = () => context.AddUserMessage("next");

        // Assert
        act.Should().NotThrow();
        context.History.Select(Describe).Should().Equal("User:opening", "User:next");
    }

    [Fact]
    public async Task RecordingCall_AfterAPrepareThrewForPinnedMessagesOverTheBudget_Succeeds()
    {
        // Arrange
        using var context = CreateContext(StubCompactionStrategy.Unchanged());
        context.AddPinnedMessage(MessageRole.User, Text("pinned", 1_200));
        await context.Awaiting(c => c.PrepareAsync()).Should().ThrowAsync<PinnedTokenBudgetExceededException>();

        // Act
        var act = () => context.AddUserMessage("next");

        // Assert
        act.Should().NotThrow();
        context.History.Select(Describe).Should().Equal("User:pinned", "User:next");
    }

    [Fact]
    public async Task ToolLoop_WhenEachCallRunsOnItsOwnThreadAfterThePreviousOneFinished_RecordsEveryMessageWithoutAnException()
    {
        // Arrange
        var strategy = new HeldStrategy();
        using var context = CreateContext(strategy);
        context.AddUserMessage(Text("opening", 600));

        // Act
        var act = async () =>
        {
            Task<PrepareResult>? pending = null;
            RunOnNewThread(() => pending = context.PrepareAsync());
            strategy.Release();
            await pending!;

            ContentSegment[] toolCalls =
            [
                new ToolUseContent("call_1", "search", "{}"),
                new ToolUseContent("call_2", "search", "{}"),
                new ToolUseContent("call_3", "search", "{}"),
            ];
            RunOnNewThread(() => context.RecordModelResponse(toolCalls));
            RunOnNewThread(() => context.RecordToolResult("call_1", "search", "result"));
            RunOnNewThread(() => context.RecordToolResult("call_2", "search", "result"));
            RunOnNewThread(() => context.RecordToolResult("call_3", "search", "result"));

            return await context.PrepareAsync();
        };

        // Assert
        var result = (await act.Should().NotThrowAsync()).Subject;
        string[] expected = ["User:opening", "Model:call_1", "Tool:call_1", "Tool:call_2", "Tool:call_3"];
        context.History.Select(Describe).Should().Equal(expected);
        result.Messages.Select(Describe).Should().Equal(expected);
    }

    private static ConversationContext CreateContext(ICompactionStrategy strategy) =>
        new(new ContextBudget(1_000, 0.5), new TextLengthTokenCounter(), strategy);

    private static void Record(IConversationContext context, string member)
    {
        switch (member)
        {
            case SetSystemPrompt:
                context.SetSystemPrompt("system");
                break;
            case AddPinnedText:
                context.AddPinnedMessage(MessageRole.User, "pinned-text");
                break;
            case AddPinnedSegments:
                context.AddPinnedMessage(MessageRole.User, [new TextContent("pinned-segments")]);
                break;
            case AddUserMessage:
                context.AddUserMessage("user");
                break;
            case RecordModelResponse:
                context.RecordModelResponse([new ToolUseContent("call_1", "search", "{}")]);
                break;
            case RecordToolResult:
                context.RecordToolResult("call_1", "search", "result");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(member), member, "Unknown recording member.");
        }
    }

    private static void RecordWithInvalidArgument(IConversationContext context, string member)
    {
        switch (member)
        {
            case SetSystemPrompt:
                context.SetSystemPrompt(" ");
                break;
            case AddPinnedText:
                context.AddPinnedMessage(MessageRole.User, " ");
                break;
            case AddPinnedSegments:
                context.AddPinnedMessage(MessageRole.User, Array.Empty<ContentSegment>());
                break;
            case AddUserMessage:
                context.AddUserMessage(" ");
                break;
            case RecordModelResponse:
                context.RecordModelResponse([]);
                break;
            case RecordToolResult:
                context.RecordToolResult(" ", "search", "result");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(member), member, "Unknown recording member.");
        }
    }

    /// <summary>
    ///     Runs an action on a thread created for it and returns after that thread has finished.
    /// </summary>
    /// <param name="action">The action to run.</param>
    private static void RunOnNewThread(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
        });

        thread.Start();
        thread.Join();
        failure?.Throw();
    }

    private static string Describe(ContextMessage message)
    {
        var label = message.Segments[0] switch
        {
            ToolUseContent toolUse => toolUse.ToolCallId,
            ToolResultContent toolResult => toolResult.ToolCallId,
            var segment => segment.Content.Split('.')[0],
        };

        return $"{message.Role}:{label}";
    }

    private static string Text(string label, int length) => label.PadRight(length, '.');

    /// <summary>
    ///     Represents a compaction strategy that stays pending until the test releases it, fails it, or cancels the call.
    /// </summary>
    /// <remarks>
    ///     A released strategy returns its input unchanged, on this call and on every later one.
    /// </remarks>
    private sealed class HeldStrategy : ICompactionStrategy
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => this._gate.SetResult();

        public void Fail(Exception exception) => this._gate.SetException(exception);

        /// <inheritdoc />
        public async Task<CompactionResult> CompactAsync(
            IReadOnlyList<ContextMessage> messages, int availableTokens, CancellationToken cancellationToken = default)
        {
            await this._gate.Task.WaitAsync(cancellationToken);

            var tokens = messages.Sum(message => message.TokenCount ?? 0);
            return new CompactionResult(messages, tokens, tokens, messagesAffected: 0, nameof(HeldStrategy));
        }
    }
}
