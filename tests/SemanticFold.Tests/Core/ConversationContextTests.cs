using SemanticFold.Core;
using SemanticFold.Core.Abstractions;
using SemanticFold.Core.Enums;
using SemanticFold.Core.Models;
using SemanticFold.Core.Models.Content;

namespace SemanticFold.Tests.Core;

public sealed class ConversationContextTests
{
    [Fact]
    public void Prepare_WhenEstimateIsBelowThreshold_ReturnsOriginalListAndSkipsCompaction()
    {
        var counter = new TrackingTokenCounter();
        var strategy = new TrackingCompactionStrategy();
        var engine = new ConversationContext(ContextBudget.For(1_000), counter, strategy);

        // AddUserMessage pre-warms the cache; set its token count to 0
        engine.AddUserMessage("hello");
        counter.Set(engine.History[0], 0);

        var prepared = engine.Prepare();

        Assert.Same(engine.History, prepared);
        Assert.Equal(0, strategy.CompactCalls);
    }

    [Fact]
    public void Prepare_WhenEstimateMeetsThreshold_UsesCompactionStrategyResult()
    {
        var compacted = Message.FromText(MessageRole.Model, "compacted");
        var counter = new TrackingTokenCounter();

        // Pre-configure so the engine caches 800 when AddUserMessage calls EnsureCached.
        counter.SetByText("original", 800);
        counter.Set(compacted, 800);

        var strategy = new TrackingCompactionStrategy([compacted]);
        var engine = new ConversationContext(ContextBudget.For(1_000), counter, strategy);

        engine.AddUserMessage("original");

        var prepared = engine.Prepare();

        Assert.Equal(1, strategy.CompactCalls);
        Assert.Same(engine.History[0], strategy.LastInput![0]);
        Assert.Same(compacted, Assert.Single(prepared));
    }

    [Fact]
    public void Prepare_WhenSystemMessagesExist_KeepsThemAtTopAndAdjustsBudget()
    {
        var compacted = Message.FromText(MessageRole.Model, "compacted");
        var counter = new TrackingTokenCounter();

        // Pre-configure counts by text before the engine creates messages, so the cache is
        // seeded with the intended values at add-time.
        counter.SetByText("sys1", 100);
        counter.SetByText("user1", 900); // 100 + 900 = 1000, triggers compaction (threshold 800)
        counter.Set(compacted, 100);

        var strategy = new TrackingCompactionStrategy([compacted]);
        var budget = ContextBudget.For(1_000); // threshold is 800
        var engine = new ConversationContext(budget, counter, strategy);

        engine.SetSystemPrompt("sys1");
        engine.AddUserMessage("user1");

        var sys1 = engine.History[0];
        var user1 = engine.History[1];

        var prepared = engine.Prepare();

        // System messages excluded from compactable messages
        Assert.Single(strategy.LastInput!);
        Assert.Same(user1, strategy.LastInput![0]);

        // Budget adjusted (reservedTokens increased by sys1 tokens)
        Assert.Equal(100, strategy.LastBudget.ReservedTokens);
        Assert.Equal(1000, strategy.LastBudget.MaxTokens);
        Assert.Equal(900, strategy.LastBudget.AvailableTokens);

        // Result: sys1 + compacted
        Assert.Equal(2, prepared.Count);
        Assert.Same(sys1, prepared[0]);
        Assert.Same(compacted, prepared[1]);
    }

    [Fact]
    public void SetSystemPrompt_InsertsAtTopAndPrepareReturnsCorrectOrder()
    {
        var counter = new TrackingTokenCounter();
        var strategy = new TrackingCompactionStrategy();
        var engine = new ConversationContext(ContextBudget.For(1_000), counter, strategy);

        engine.AddUserMessage("user1");
        engine.SetSystemPrompt("sys1");

        var sys1 = engine.History.First(m => m.Role == MessageRole.System);
        var user1 = engine.History.First(m => m.Role == MessageRole.User);

        counter.Set(sys1, 100);
        counter.Set(user1, 100);

        var prepared = engine.Prepare();

        Assert.Equal(2, prepared.Count);
        Assert.Same(sys1, prepared[0]);
        Assert.Equal(0, strategy.CompactCalls);
    }

    [Fact]
    public void RecordModelResponse_PrewarmsCache_SoPrepareDoesNotRecountSameMessage()
    {
        var counter = new TrackingTokenCounter();
        var strategy = new TrackingCompactionStrategy();
        var engine = new ConversationContext(ContextBudget.For(1_000), counter, strategy);

        engine.RecordModelResponse([new TextContent("hello")]);
        var message = engine.History[0];
        counter.Set(message, 1);

        _ = engine.Prepare();

        Assert.Equal(1, counter.GetCountCalls(message));
    }

    [Fact]
    public void RecordModelResponse_WithProviderInputTokens_AppliesAnchorCorrectionOnNextPrepare()
    {
        var counter = new TrackingTokenCounter();
        var strategy = new TrackingCompactionStrategy([Message.FromText(MessageRole.Model, "compressed")]);
        var engine = new ConversationContext(ContextBudget.For(1_000), counter, strategy);

        engine.AddUserMessage("hello");
        counter.Set(engine.History[0], 0);

        _ = engine.Prepare();

        engine.RecordModelResponse([new TextContent("reply")], providerInputTokens: 800);
        counter.Set(engine.History[1], 0);

        _ = engine.Prepare();

        Assert.Equal(1, strategy.CompactCalls);
    }

    [Fact]
    public void Prepare_CacheUsesReferenceIdentity_ForEquivalentButDistinctMessages()
    {
        var counter = new TrackingTokenCounter();
        var strategy = new TrackingCompactionStrategy();
        var engine = new ConversationContext(ContextBudget.For(1_000), counter, strategy);

        engine.AddUserMessage("same");
        engine.AddUserMessage("same");

        var first = engine.History[0];
        var second = engine.History[1];
        counter.Set(first, 0);
        counter.Set(second, 0);

        _ = engine.Prepare();

        Assert.Equal(1, counter.GetCountCalls(first));
        Assert.Equal(1, counter.GetCountCalls(second));
    }

    [Fact]
    public void Prepare_CachesTokenCountOnCompactedMessages_SoTheyAreNotRecounted()
    {
        var compacted = Message.FromText(MessageRole.Model, "compacted");
        var counter = new TrackingTokenCounter();

        // Pre-configure the original message count by text so the engine caches 800 at add-time.
        counter.SetByText("original", 800);
        counter.Set(compacted, 800);

        var strategy = new TrackingCompactionStrategy([compacted]);
        var engine = new ConversationContext(ContextBudget.For(1_000), counter, strategy);

        engine.AddUserMessage("original");

        // First Prepare triggers compaction; compacted message counted once and cached
        _ = engine.Prepare();

        // Second Prepare: engine history is still the original message (800 tokens), so compaction
        // fires again. compacted is not counted a second time because its TokenCount property is set.
        _ = engine.Prepare();

        Assert.Equal(1, counter.GetCountCalls(compacted));
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

        // Fallback map keyed by the first text content of a message, for pre-configuration before
        // message references are available.
        private readonly Dictionary<string, int> _countsByText = new();

        public void Set(Message message, int count)
        {
            this._counts[message] = count;
        }

        /// <summary>
        /// Registers a token count returned whenever a message's first text block matches
        /// <paramref name="text"/>. Useful for pre-configuring counts before the engine
        /// creates message references.
        /// </summary>
        public void SetByText(string text, int count)
        {
            this._countsByText[text] = count;
        }

        public int GetCountCalls(Message message)
        {
            return this._calls.GetValueOrDefault(message, 0);
        }

        public int Count(Message message)
        {
            this._calls[message] = this.GetCountCalls(message) + 1;

            if (this._counts.TryGetValue(message, out var cached))
                return cached;

            var firstText = message.Content.OfType<SemanticFold.Core.Models.Content.TextContent>().FirstOrDefault()?.Text;
            if (firstText != null && this._countsByText.TryGetValue(firstText, out var byText))
                return byText;

            return 0;
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
