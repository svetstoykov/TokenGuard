using SemanticFold.Abstractions;
using SemanticFold.Enums;
using SemanticFold.Models;

namespace SemanticFold.Tests.Core;

public sealed class FoldingEngineTests
{
    [Fact]
    public void Prepare_WhenEstimateIsBelowThreshold_ReturnsOriginalListAndSkipsCompaction()
    {
        var message = Message.FromText(MessageRole.User, "hello");
        var messages = new List<Message> { message };
        var counter = new TrackingTokenCounter();
        counter.Set(message, 0);
        var strategy = new TrackingCompactionStrategy();
        var engine = new FoldingEngine(ContextBudget.For(1_000), counter, strategy);

        var prepared = engine.Prepare(messages);

        Assert.Same(messages, prepared);
        Assert.Equal(0, strategy.CompactCalls);
    }

    [Fact]
    public void Prepare_WhenEstimateMeetsThreshold_UsesCompactionStrategyResult()
    {
        var original = Message.FromText(MessageRole.User, "original");
        var compacted = Message.FromText(MessageRole.Model, "compacted");
        var messages = new List<Message> { original };
        var counter = new TrackingTokenCounter();
        counter.Set(original, 800);
        counter.Set(compacted, 800);
        var strategy = new TrackingCompactionStrategy([compacted]);
        var engine = new FoldingEngine(ContextBudget.For(1_000), counter, strategy);

        var prepared = engine.Prepare(messages);

        Assert.Equal(1, strategy.CompactCalls);
        Assert.Same(messages, strategy.LastInput);
        Assert.Same(compacted, Assert.Single(prepared));
    }

    [Fact]
    public void Prepare_WhenSystemMessagesExist_KeepsThemAtTopAndAdjustsBudget()
    {
        var sys1 = Message.FromText(MessageRole.System, "sys1");
        var user1 = Message.FromText(MessageRole.User, "user1");
        var compacted = Message.FromText(MessageRole.Model, "compacted");

        var messages = new List<Message> { sys1, user1 };
        var counter = new TrackingTokenCounter();
        counter.Set(sys1, 100);
        counter.Set(user1, 900); // 100 + 900 = 1000, which triggers compaction (threshold is 800)
        counter.Set(compacted, 100);

        var strategy = new TrackingCompactionStrategy([compacted]);
        var budget = ContextBudget.For(1_000); // threshold is 800
        var engine = new FoldingEngine(budget, counter, strategy);

        var prepared = engine.Prepare(messages);

        // System messages are excluded from compactable messages
        Assert.Single(strategy.LastInput!);
        Assert.Same(user1, strategy.LastInput[0]);

        // Budget is adjusted (reservedTokens increased by sys1 tokens)
        Assert.Equal(100, strategy.LastBudget.ReservedTokens);
        Assert.Equal(1000, strategy.LastBudget.MaxTokens);
        Assert.Equal(900, strategy.LastBudget.AvailableTokens);

        // Result should be sys1 + compacted
        Assert.Equal(2, prepared.Count);
        Assert.Same(sys1, prepared[0]);
        Assert.Same(compacted, prepared[1]);
    }

    [Fact]
    public void Prepare_WhenSystemMessagesAreNotAtTop_AndNoCompaction_ReordersThem()
    {
        var sys1 = Message.FromText(MessageRole.System, "sys1");
        var user1 = Message.FromText(MessageRole.User, "user1");

        var messages = new List<Message> { user1, sys1 };
        var counter = new TrackingTokenCounter();
        counter.Set(sys1, 100);
        counter.Set(user1, 100);

        var strategy = new TrackingCompactionStrategy();
        var budget = ContextBudget.For(1_000); // threshold is 800, total 200 is below threshold
        var engine = new FoldingEngine(budget, counter, strategy);

        var prepared = engine.Prepare(messages);

        Assert.Equal(2, prepared.Count);
        Assert.Same(sys1, prepared[0]);
        Assert.Same(user1, prepared[1]);
        Assert.Equal(0, strategy.CompactCalls);
    }

    [Fact]
    public void ObserveSingle_PrewarmsCache_SoPrepareDoesNotRecountSameMessage()
    {
        var message = Message.FromText(MessageRole.User, "hello");
        var messages = new List<Message> { message };
        var counter = new TrackingTokenCounter();
        counter.Set(message, 1);
        var strategy = new TrackingCompactionStrategy(messages);
        var engine = new FoldingEngine(ContextBudget.For(1_000), counter, strategy);

        engine.Observe(message);
        _ = engine.Prepare(messages);

        Assert.Equal(1, counter.GetCountCalls(message));
    }

    [Fact]
    public void ObserveBatch_WithApiReportedTokens_AppliesAnchorCorrectionOnNextPrepare()
    {
        var message = Message.FromText(MessageRole.User, "hello");
        var messages = new List<Message> { message };
        var counter = new TrackingTokenCounter();
        counter.Set(message, 0);
        var strategy = new TrackingCompactionStrategy([Message.FromText(MessageRole.Model, "compressed")]);
        var engine = new FoldingEngine(ContextBudget.For(1_000), counter, strategy);

        _ = engine.Prepare(messages);
        engine.Observe(messages, apiReportedInputTokens: 800);
        _ = engine.Prepare(messages);

        Assert.Equal(1, strategy.CompactCalls);
    }

    [Fact]
    public void Prepare_CacheUsesReferenceIdentity_ForEquivalentButDistinctMessages()
    {
        var first = Message.FromText(MessageRole.User, "same");
        var second = Message.FromText(MessageRole.User, "same");
        var messages = new List<Message> { first, second };
        var counter = new TrackingTokenCounter();
        counter.Set(first, 0);
        counter.Set(second, 0);
        var strategy = new TrackingCompactionStrategy();
        var engine = new FoldingEngine(ContextBudget.For(1_000), counter, strategy);

        _ = engine.Prepare(messages);

        Assert.Equal(1, counter.GetCountCalls(first));
        Assert.Equal(1, counter.GetCountCalls(second));
    }

    [Fact]
    public void Prepare_DoesNotCacheCompactedMessagesDuringInternalTotalCalculation()
    {
        var original = Message.FromText(MessageRole.User, "original");
        var compacted = Message.FromText(MessageRole.Model, "compacted");
        var input = new List<Message> { original };
        var compactedList = new List<Message> { compacted };
        var counter = new TrackingTokenCounter();
        counter.Set(original, 800);
        counter.Set(compacted, 800);
        var strategy = new TrackingCompactionStrategy(compactedList);
        var engine = new FoldingEngine(ContextBudget.For(1_000), counter, strategy);

        _ = engine.Prepare(input);
        _ = engine.Prepare(compactedList);

        Assert.Equal(2, counter.GetCountCalls(compacted));
    }

    private sealed class TrackingCompactionStrategy : ICompactionStrategy
    {
        private readonly IReadOnlyList<Message>? _result;

        public TrackingCompactionStrategy(IReadOnlyList<Message>? result = null)
        {
            this._result = result;
        }

        public int CompactCalls { get; private set; }

        public IReadOnlyList<Message>? LastInput { get; private set; }

        public ContextBudget LastBudget { get; private set; }

        public IReadOnlyList<Message> Compact(IReadOnlyList<Message> messages, ContextBudget budget, ITokenCounter tokenCounter)
        {
            this.CompactCalls++;
            this.LastInput = messages;
            this.LastBudget = budget;

            return this._result ?? messages;
        }
    }

    private sealed class TrackingTokenCounter : ITokenCounter
    {
        private readonly Dictionary<Message, int> _counts = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<Message, int> _calls = new(ReferenceEqualityComparer.Instance);

        public void Set(Message message, int count)
        {
            this._counts[message] = count;
        }

        public int GetCountCalls(Message message)
        {
            return this._calls.GetValueOrDefault(message, 0);
        }

        public int Count(Message message)
        {
            this._calls[message] = this.GetCountCalls(message) + 1;
            return this._counts.GetValueOrDefault(message, 0);
        }

        public int Count(IEnumerable<Message> messages)
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

    private sealed class ReferenceEqualityComparer : IEqualityComparer<Message>
    {
        public static readonly ReferenceEqualityComparer Instance = new();

        public bool Equals(Message? x, Message? y)
        {
            return ReferenceEquals(x, y);
        }

        public int GetHashCode(Message obj)
        {
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
