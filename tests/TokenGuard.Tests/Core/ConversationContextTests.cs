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
        var counter = new TrackingTokenCounter();
        var strategy = new TrackingCompactionStrategy();
        var engine = new ConversationContext(ContextBudget.For(1_000), counter, strategy);

        // AddUserMessage pre-warms the cache; set its token count to 0
        engine.AddUserMessage("hello");
        counter.Set(engine.History[0], 0);

        var prepared = await engine.PrepareAsync();

        Assert.Same(engine.History, prepared);
        Assert.Equal(0, strategy.CompactCalls);
    }

    [Fact]
    public async Task PrepareAsync_WhenEstimateMeetsThreshold_UsesCompactionStrategyResult()
    {
        var compacted = SemanticMessage.FromText(MessageRole.Model, "compacted");
        var counter = new TrackingTokenCounter();

        // Pre-configure so the engine caches 800 when AddUserMessage calls EnsureCached.
        counter.SetByText("original", 800);
        counter.Set(compacted, 800);

        var strategy = new TrackingCompactionStrategy([compacted]);
        var engine = new ConversationContext(ContextBudget.For(1_000), counter, strategy);

        engine.AddUserMessage("original");

        var prepared = await engine.PrepareAsync();

        Assert.Equal(1, strategy.CompactCalls);
        Assert.Same(engine.History[0], strategy.LastInput![0]);
        Assert.Same(compacted, Assert.Single(prepared));
    }

    [Fact]
    public async Task PrepareAsync_WhenSystemMessagesExist_KeepsThemAtTopAndAdjustsBudget()
    {
        var compacted = SemanticMessage.FromText(MessageRole.Model, "compacted");
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

        var prepared = await engine.PrepareAsync();

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
    public async Task SetSystemPrompt_InsertsAtTopAndPrepareAsyncReturnsCorrectOrder()
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

        var prepared = await engine.PrepareAsync();

        Assert.Equal(2, prepared.Count);
        Assert.Same(sys1, prepared[0]);
        Assert.Equal(0, strategy.CompactCalls);
    }

    [Fact]
    public async Task RecordModelResponse_PrewarmsCache_SoPrepareAsyncDoesNotRecountSameMessage()
    {
        var counter = new TrackingTokenCounter();
        var strategy = new TrackingCompactionStrategy();
        var engine = new ConversationContext(ContextBudget.For(1_000), counter, strategy);

        engine.RecordModelResponse([new TextContent("hello")]);
        var message = engine.History[0];
        counter.Set(message, 1);

        _ = await engine.PrepareAsync();

        Assert.Equal(1, counter.GetCountCalls(message));
    }

    [Fact]
    public async Task RecordModelResponse_WithProviderInputTokens_AppliesAnchorCorrectionOnNextPrepareAsync()
    {
        var counter = new TrackingTokenCounter();
        var strategy = new TrackingCompactionStrategy([SemanticMessage.FromText(MessageRole.Model, "compressed")]);
        var engine = new ConversationContext(ContextBudget.For(1_000), counter, strategy);

        engine.AddUserMessage("hello");
        counter.Set(engine.History[0], 0);

        _ = await engine.PrepareAsync();

        engine.RecordModelResponse([new TextContent("reply")], providerInputTokens: 800);
        counter.Set(engine.History[1], 0);

        _ = await engine.PrepareAsync();

        Assert.Equal(1, strategy.CompactCalls);
    }

    [Fact]
    public async Task PrepareAsync_CacheUsesReferenceIdentity_ForEquivalentButDistinctMessages()
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

        _ = await engine.PrepareAsync();

        Assert.Equal(1, counter.GetCountCalls(first));
        Assert.Equal(1, counter.GetCountCalls(second));
    }

    [Fact]
    public async Task PrepareAsync_CachesTokenCountOnCompactedMessages_SoTheyAreNotRecounted()
    {
        var compacted = SemanticMessage.FromText(MessageRole.Model, "compacted");
        var counter = new TrackingTokenCounter();

        // Pre-configure the original message count by text so the engine caches 800 at add-time.
        counter.SetByText("original", 800);
        counter.Set(compacted, 800);

        var strategy = new TrackingCompactionStrategy([compacted]);
        var engine = new ConversationContext(ContextBudget.For(1_000), counter, strategy);

        engine.AddUserMessage("original");

        // First Prepare triggers compaction; compacted message counted once and cached
        _ = await engine.PrepareAsync();

        // Second Prepare: engine history is still the original message (800 tokens), so compaction
        // fires again. compacted is not counted a second time because its TokenCount property is set.
        _ = await engine.PrepareAsync();

        Assert.Equal(1, counter.GetCountCalls(compacted));
    }

    private sealed class TrackingCompactionStrategy : ICompactionStrategy
    {
        private readonly IReadOnlyList<SemanticMessage>? _result;

        public TrackingCompactionStrategy(IReadOnlyList<SemanticMessage>? result = null)
        {
            this._result = result;
        }

        public int CompactCalls { get; private set; }

        public IReadOnlyList<SemanticMessage>? LastInput { get; private set; }

        public ContextBudget LastBudget { get; private set; }

        public Task<IReadOnlyList<SemanticMessage>> CompactAsync(IReadOnlyList<SemanticMessage> messages, ContextBudget budget, ITokenCounter tokenCounter, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.CompactCalls++;
            this.LastInput = messages;
            this.LastBudget = budget;

            return Task.FromResult(this._result ?? messages);
        }
    }

    private sealed class TrackingTokenCounter : ITokenCounter
    {
        private readonly Dictionary<SemanticMessage, int> _counts = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<SemanticMessage, int> _calls = new(ReferenceEqualityComparer.Instance);

        // Fallback map keyed by the first text content of a message, for pre-configuration before
        // message references are available.
        private readonly Dictionary<string, int> _countsByText = new();

        public void Set(SemanticMessage semanticMessage, int count)
        {
            this._counts[semanticMessage] = count;
        }

        /// <summary>
        /// Registers a token count returned whenever a message's first text segment matches
        /// <paramref name="text"/>. Useful for pre-configuring counts before the engine
        /// creates message references.
        /// </summary>
        public void SetByText(string text, int count)
        {
            this._countsByText[text] = count;
        }

        public int GetCountCalls(SemanticMessage semanticMessage)
        {
            return this._calls.GetValueOrDefault(semanticMessage, 0);
        }

        public int Count(SemanticMessage semanticMessage)
        {
            this._calls[semanticMessage] = this.GetCountCalls(semanticMessage) + 1;

            if (this._counts.TryGetValue(semanticMessage, out var cached))
                return cached;

            var firstText = semanticMessage.Content.OfType<TokenGuard.Core.Models.Content.TextContent>().FirstOrDefault()?.Text;
            if (firstText != null && this._countsByText.TryGetValue(firstText, out var byText))
                return byText;

            return 0;
        }

        public int Count(IEnumerable<SemanticMessage> messages)
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

    private sealed class ReferenceEqualityComparer : IEqualityComparer<SemanticMessage>
    {
        public static readonly ReferenceEqualityComparer Instance = new();

        public bool Equals(SemanticMessage? x, SemanticMessage? y)
        {
            return ReferenceEquals(x, y);
        }

        public int GetHashCode(SemanticMessage obj)
        {
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
