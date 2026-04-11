using FluentAssertions;
using TokenGuard.Core;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;

namespace TokenGuard.Tests.Core;

public sealed class ConversationContextTests
{
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
        var prepared = await engine.PrepareAsync();

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

        var strategy = new TrackingCompactionStrategy(new CompactionResult([compacted], 800, 800, 1, "TestStrategy", true));
        var engine = new ConversationContext(ContextBudget.For(1_000), counter, strategy);

        engine.AddUserMessage("original");

        // Act
        var prepared = await engine.PrepareAsync();

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

        var strategy = new TrackingCompactionStrategy(new CompactionResult([compacted], 900, 100, 1, "TestStrategy", true));
        var budget = ContextBudget.For(1_000);
        var engine = new ConversationContext(budget, counter, strategy);

        engine.SetSystemPrompt("sys1");
        engine.AddUserMessage("user1");

        var sys1 = engine.History[0];
        var user1 = engine.History[1];

        // Act
        var prepared = await engine.PrepareAsync();

        // Assert
        strategy.LastInput.Should().ContainSingle().Which.Should().BeSameAs(user1);
        strategy.LastBudget.ReservedTokens.Should().Be(100);
        strategy.LastBudget.MaxTokens.Should().Be(1000);
        strategy.LastBudget.AvailableTokens.Should().Be(900);
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
        var prepared = await engine.PrepareAsync();

        // Assert
        prepared.Should().HaveCount(2);
        prepared[0].Should().BeSameAs(sys1);
        strategy.CompactCalls.Should().Be(0);
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
        _ = await engine.PrepareAsync();

        // Assert
        counter.GetCountCalls(message).Should().Be(1);
    }

    [Fact]
    public async Task RecordModelResponse_WithProviderInputTokens_AppliesAnchorCorrectionOnNextPrepareAsync()
    {
        // Arrange
        var counter = new TrackingTokenCounter();
        var strategy = new TrackingCompactionStrategy(new CompactionResult([ContextMessage.FromText(MessageRole.Model, "compressed")], 800, 100, 1, "TestStrategy", true));
        var engine = new ConversationContext(ContextBudget.For(1_000), counter, strategy);

        engine.AddUserMessage("hello");
        counter.Set(engine.History[0], 0);

        _ = await engine.PrepareAsync();

        engine.RecordModelResponse([new TextContent("reply")], providerInputTokens: 800);
        counter.Set(engine.History[1], 0);

        // Act
        _ = await engine.PrepareAsync();

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
        var prepared = await engine.PrepareAsync();

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

        _ = await engine.PrepareAsync();

        engine.RecordModelResponse([new TextContent("m1")], providerInputTokens: 80);
        counter.Set(engine.History[1], 50);

        var firstPrepared = await engine.PrepareAsync();

        engine.RecordModelResponse([new TextContent("m2")], providerInputTokens: 90);
        counter.Set(engine.History[2], 20);

        var secondPrepared = await engine.PrepareAsync();

        engine.RecordModelResponse([new TextContent("m3")], providerInputTokens: 95);
        counter.Set(engine.History[3], 10);

        // Act
        var thirdPrepared = await engine.PrepareAsync();

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
        _ = await engine.PrepareAsync();

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

        var strategy = new TrackingCompactionStrategy(new CompactionResult([compacted], 800, 800, 1, "TestStrategy", true));
        var engine = new ConversationContext(ContextBudget.For(1_000), counter, strategy);

        engine.AddUserMessage("original");

        _ = await engine.PrepareAsync();

        // Act
        _ = await engine.PrepareAsync();

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
            "AddUserMessage"      => Sync(() => engine.AddUserMessage("x")),
            "RecordModelResponse" => Sync(() => engine.RecordModelResponse([new TextContent("x")])),
            "RecordToolResult"    => Sync(() => engine.RecordToolResult("id", "tool", "x")),
            "PrepareAsync"        => engine.PrepareAsync(),
            _                     => throw new ArgumentOutOfRangeException(nameof(member))
        };

        // Assert
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    private static Task Sync(Action action)
    {
        action();
        return Task.CompletedTask;
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
        /// Gets the last budget passed to <see cref="CompactAsync"/>.
        /// </summary>
        public ContextBudget LastBudget { get; private set; }

        /// <summary>
        /// Records the compaction request and returns the configured result.
        /// </summary>
        /// <param name="messages">The messages selected for compaction.</param>
        /// <param name="budget">The budget available to the compaction operation.</param>
        /// <param name="tokenCounter">The token counter associated with the request.</param>
        /// <param name="cancellationToken">A token used to cancel the operation.</param>
        /// <returns>A task containing either the configured result or a pass-through compaction result.</returns>
        public Task<CompactionResult> CompactAsync(IReadOnlyList<ContextMessage> messages, ContextBudget budget, ITokenCounter tokenCounter, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.CompactCalls++;
            this.LastInput = messages;
            this.LastBudget = budget;

            return Task.FromResult(this._result ?? new CompactionResult(messages, 0, 0, 0, nameof(TrackingCompactionStrategy), false));
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

            var firstText = contextMessage.Content.OfType<TokenGuard.Core.Models.Content.TextContent>().FirstOrDefault()?.Content;
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

            var total = 0;
            foreach (var message in messages)
            {
                total += this.Count(message);
            }

            return total;
        }
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
