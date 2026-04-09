using SemanticFold.Abstractions;
using SemanticFold.Enums;
using SemanticFold.Models;

namespace SemanticFold;

/// <summary>
/// The core SemanticFold engine. Sits inside an agent loop and manages conversation context
/// automatically by monitoring token usage and applying a compaction strategy when needed.
///
/// <para>Two touch points in the loop:</para>
/// <list type="bullet">
///   <item><description>
///     <see cref="Prepare"/> — call before every LLM request. Returns the message list to send
///     (compacted if over threshold, original otherwise). Never modifies the caller's list.
///   </description></item>
///   <item><description>
///     <see cref="Observe(Message, int?)"/> / <see cref="Observe(IReadOnlyList{Message}, int?)"/> —
///     call after the API response and after tool execution to pre-warm the token cache and
///     optionally anchor the running total to the provider's ground-truth count.
///   </description></item>
/// </list>
/// </summary>
public sealed class FoldingEngine
{
    private readonly ContextBudget _budget;
    private readonly ITokenCounter _counter;
    private readonly ICompactionStrategy _strategy;

    // Keyed by reference so two structurally equal Message records are treated as distinct entries.
    private readonly Dictionary<Message, int> _tokenCache = new(ReferenceEqualityComparer.Instance);

    // Token total of the list most recently returned by Prepare — used to compute anchor corrections.
    private int _lastPreparedTotal;

    // Additive correction applied to every raw estimate to account for systematic estimator drift.
    // Updated each time Observe is called with an apiReportedInputTokens value.
    private int _anchorCorrection;

    /// <summary>
    /// Initializes a new <see cref="SemanticFold"/> engine.
    /// </summary>
    /// <param name="budget">
    /// The token budget governing when compaction triggers. Use <see cref="ContextBudget.For"/>
    /// to construct one from a raw max-token value, or set thresholds explicitly.
    /// </param>
    /// <param name="counter">
    /// The token counter used to size messages. Defaults to <see cref="strategy"/>
    /// in the builder; supply a custom implementation for provider-accurate counting.
    /// </param>
    /// <param name="strategy">
    /// The compaction strategy applied when the budget threshold is exceeded.
    /// </param>
    public FoldingEngine(ContextBudget budget, ITokenCounter counter, ICompactionStrategy strategy)
    {
        this._budget = budget;
        this._counter = counter;
        this._strategy = strategy;
    }

    /// <summary>
    /// Evaluates the current context size and returns a managed message list ready to send to the LLM.
    ///
    /// <para>
    /// If the estimated token total is within the compaction threshold, the original list is
    /// returned as-is (no allocation). If the threshold is met or exceeded, the configured
    /// strategy is applied and the resulting compacted list is returned. The caller's original
    /// list is never modified.
    /// </para>
    ///
    /// <para>Example usage:</para>
    /// <code>
    /// var messagesToSend = fold.Prepare(messages);
    /// var response = await client.SendAsync(messagesToSend);
    /// </code>
    /// </summary>
    /// <param name="messages">The full conversation history owned by the caller.</param>
    /// <returns>
    /// The original list if token usage is within the compaction threshold; otherwise a new,
    /// compacted list produced by the configured strategy.
    /// </returns>
    public IReadOnlyList<Message> Prepare(IReadOnlyList<Message> messages)
    {
        var total = this.SumWithCaching(messages) + this._anchorCorrection;

        if (total < this._budget.CompactionTriggerTokens)
        {
            this._lastPreparedTotal = total;
            
            if (HasMisplacedSystemMessages(messages))
            {
                var systemMessages = messages.Where(m => m.Role == MessageRole.System);
                var otherMessages = messages.Where(m => m.Role != MessageRole.System);
                return systemMessages.Concat(otherMessages).ToList();
            }
            
            return messages;
        }

        var sysMsgs = messages.Where(m => m.Role == MessageRole.System).ToList();
        var compactableMessages = sysMsgs.Count == 0 ? messages : messages.Where(m => m.Role != MessageRole.System).ToList();
        var systemTotal = this.SumWithCaching(sysMsgs);
        
        var adjustedBudget = new ContextBudget(
            this._budget.MaxTokens,
            this._budget.CompactionThreshold,
            this._budget.EmergencyThreshold,
            this._budget.ReservedTokens + systemTotal
        );

        var compacted = this._strategy.Compact(compactableMessages, adjustedBudget, this._counter);

        var result = sysMsgs.Count == 0 ? compacted : sysMsgs.Concat(compacted).ToList();

        // Compacted messages are ephemeral instances owned by the strategy, not the caller.
        // Count them without caching to avoid retaining strong references indefinitely.
        this._lastPreparedTotal = this.SumWithoutCaching(result) + this._anchorCorrection;

        return result;
    }

    private static bool HasMisplacedSystemMessages(IReadOnlyList<Message> messages)
    {
        bool seenNonSystem = false;
        foreach (var m in messages)
        {
            if (m.Role == MessageRole.System)
            {
                if (seenNonSystem) return true;
            }
            else
            {
                seenNonSystem = true;
            }
        }
        return false;
    }

    /// <summary>
    /// Registers a single new message with the engine and pre-warms the token cache for it,
    /// so the next <see cref="Prepare"/> call does not need to re-estimate it.
    ///
    /// <para>
    /// Call this with the assistant response message after each API call. Optionally provide
    /// <paramref name="apiReportedInputTokens"/> to anchor the engine's running estimate to
    /// the provider's ground-truth count, correcting estimator drift going forward.
    /// </para>
    /// </summary>
    /// <param name="message">The new message to register (typically the assistant response).</param>
    /// <param name="apiReportedInputTokens">
    /// The exact input token count returned by the provider for the most recent request.
    /// When provided, the engine computes and stores a correction applied to all future
    /// <see cref="Prepare"/> evaluations. Pass <see langword="null"/> to skip anchoring.
    /// </param>
    public void Observe(Message message, int? apiReportedInputTokens = null)
    {
        this.EnsureCached(message);
        this.ApplyAnchor(apiReportedInputTokens);
    }

    /// <summary>
    /// Registers a batch of new messages with the engine and pre-warms the token cache for
    /// all of them.
    ///
    /// <para>
    /// Call this with tool result messages after each round of tool execution, before the
    /// next <see cref="Prepare"/> call. Optionally anchor to provider-reported ground truth.
    /// </para>
    ///
    /// <para>Example usage:</para>
    /// <code>
    /// var toolResults = await ExecuteTools(response.ToolCalls);
    /// messages.AddRange(toolResults);
    /// fold.Observe(toolResults, response.Usage.InputTokens);
    /// </code>
    /// </summary>
    /// <param name="messages">The new messages to register (typically tool results).</param>
    /// <param name="apiReportedInputTokens">
    /// The exact input token count returned by the provider for the most recent request.
    /// Pass <see langword="null"/> to skip anchoring.
    /// </param>
    public void Observe(IReadOnlyList<Message> messages, int? apiReportedInputTokens = null)
    {
        foreach (var message in messages)
            this.EnsureCached(message);

        this.ApplyAnchor(apiReportedInputTokens);
    }

    private int SumWithCaching(IReadOnlyList<Message> messages) => messages.Sum(this.EnsureCached);

    private int SumWithoutCaching(IReadOnlyList<Message> messages) 
        => messages.Sum(message => this._tokenCache.TryGetValue(message, out var cached) 
            ? cached 
            : this._counter.Count(message));

    private int EnsureCached(Message message)
    {
        if (!this._tokenCache.TryGetValue(message, out var count))
            this._tokenCache[message] = count = this._counter.Count(message);
        
        return count;
    }

    private void ApplyAnchor(int? apiReportedInputTokens)
    {
        if (apiReportedInputTokens.HasValue)
            this._anchorCorrection = apiReportedInputTokens.Value - this._lastPreparedTotal;
    }
}