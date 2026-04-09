using SemanticFold.Abstractions;
using SemanticFold.Enums;
using SemanticFold.Models;
using SemanticFold.Models.Content;

namespace SemanticFold;

/// <summary>
/// The core SemanticFold engine. Owns the full conversation history and manages context
/// automatically by monitoring token usage and applying a compaction strategy when needed.
///
/// <para>Entry points for adding turns:</para>
/// <list type="bullet">
///   <item><description>
///     <see cref="SetSystemPrompt"/> — sets or replaces the single system message.
///   </description></item>
///   <item><description>
///     <see cref="AddUserMessage"/> — appends a user turn.
///   </description></item>
///   <item><description>
///     <see cref="RecordModelResponse"/> — records the model's reply (text and/or tool-use blocks).
///   </description></item>
///   <item><description>
///     <see cref="RecordToolResult"/> — records one tool execution result.
///   </description></item>
/// </list>
///
/// <para>
///   Call <see cref="Prepare"/> before every LLM request to obtain the message list to send,
///   compacted if over threshold.
/// </para>
/// </summary>
public sealed class FoldingEngine
{
    private readonly ContextBudget _budget;
    private readonly ITokenCounter _counter;
    private readonly ICompactionStrategy _strategy;

    private readonly List<Message> _history = [];

    // Token total of the list most recently returned by Prepare — used to compute anchor corrections.
    private int _lastPreparedTotal;

    // Additive correction applied to every raw estimate to account for systematic estimator drift.
    // Updated each time RecordModelResponse is called with a providerInputTokens value.
    private int _anchorCorrection;

    /// <summary>
    /// Initializes a new <see cref="FoldingEngine"/>.
    /// </summary>
    /// <param name="budget">
    /// The token budget governing when compaction triggers. Use <see cref="ContextBudget.For"/>
    /// to construct one from a raw max-token value, or set thresholds explicitly.
    /// </param>
    /// <param name="counter">
    /// The token counter used to size messages. Supply a custom implementation for
    /// provider-accurate counting.
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
    /// Gets a read-only view of the full uncompacted conversation history for debugging,
    /// logging, and testing. This is a live view — modifications to the engine are reflected
    /// immediately. The caller cannot mutate it.
    /// </summary>
    public IReadOnlyList<Message> History => this._history;

    /// <summary>
    /// Sets or replaces the system prompt. The engine holds exactly one system message;
    /// calling this again overwrites the previous one. Can be called at any time.
    /// </summary>
    /// <param name="text">The system prompt text.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="text"/> is null or whitespace.</exception>
    public void SetSystemPrompt(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("System prompt text cannot be null or whitespace.", nameof(text));

        var message = Message.FromText(MessageRole.System, text);

        var existing = this._history.FindIndex(m => m.Role == MessageRole.System);
        if (existing >= 0)
            this._history[existing] = message;
        else
            this._history.Insert(0, message);

        this.EnsureCounted(message);
    }

    /// <summary>
    /// Appends a user turn to the conversation history.
    /// </summary>
    /// <param name="text">The user message text.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="text"/> is null or whitespace.</exception>
    public void AddUserMessage(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("User message text cannot be null or whitespace.", nameof(text));

        var message = Message.FromText(MessageRole.User, text);
        this._history.Add(message);
        this.EnsureCounted(message);
    }

    /// <summary>
    /// Records what the model returned and pre-warms the token count for the new message.
    /// </summary>
    /// <param name="content">
    /// The content blocks returned by the model. May contain a mix of
    /// <see cref="TextContent"/> and <see cref="ToolUseContent"/> blocks.
    /// </param>
    /// <param name="providerInputTokens">
    /// The exact input token count reported by the provider for this request. When provided,
    /// the engine computes and stores a correction applied to all future <see cref="Prepare"/>
    /// evaluations. Pass <see langword="null"/> to skip anchoring.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="content"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="content"/> is empty.</exception>
    public void RecordModelResponse(IEnumerable<ContentBlock> content, int? providerInputTokens = null)
    {
        ArgumentNullException.ThrowIfNull(content);

        var blocks = content.ToArray();
        if (blocks.Length == 0)
            throw new ArgumentException("Content must contain at least one block.", nameof(content));

        var message = new Message { Role = MessageRole.Model, Content = blocks };
        this._history.Add(message);
        this.EnsureCounted(message);
        this.ApplyAnchor(providerInputTokens);
    }

    /// <summary>
    /// Records a single tool execution result. The engine wraps this into a
    /// <see cref="MessageRole.Tool"/>-role message internally. Call once per tool call.
    /// </summary>
    /// <param name="toolCallId">The tool call identifier this result corresponds to.</param>
    /// <param name="toolName">The name of the tool that produced this result.</param>
    /// <param name="content">The tool output payload.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="toolCallId"/>, <paramref name="toolName"/>, or
    /// <paramref name="content"/> is null or whitespace.
    /// </exception>
    public void RecordToolResult(string toolCallId, string toolName, string content)
    {
        if (string.IsNullOrWhiteSpace(toolCallId))
            throw new ArgumentException("Tool call id cannot be null or whitespace.", nameof(toolCallId));

        if (string.IsNullOrWhiteSpace(toolName))
            throw new ArgumentException("Tool name cannot be null or whitespace.", nameof(toolName));

        ArgumentNullException.ThrowIfNull(content);

        var message = Message.FromContent(MessageRole.Tool, new ToolResultContent(toolCallId, toolName, content));
        this._history.Add(message);
        this.EnsureCounted(message);
    }

    /// <summary>
    /// Evaluates the current context size and returns a managed message list ready to send to the LLM.
    ///
    /// <para>
    /// If the estimated token total is within the compaction threshold, the full history is
    /// returned as-is. If the threshold is met or exceeded, the configured strategy is applied
    /// and the resulting compacted list is returned.
    /// </para>
    /// </summary>
    /// <returns>
    /// The full history if token usage is within the compaction threshold; otherwise a new,
    /// compacted list produced by the configured strategy.
    /// </returns>
    public IReadOnlyList<Message> Prepare()
    {
        var messages = (IReadOnlyList<Message>)this._history;
        var total = this.Sum(messages) + this._anchorCorrection;

        if (total < this._budget.CompactionTriggerTokens)
        {
            this._lastPreparedTotal = total;
            return messages;
        }

        var sysMsgs = messages.Where(m => m.Role == MessageRole.System).ToList();
        var compactableMessages = sysMsgs.Count == 0 ? messages : messages.Where(m => m.Role != MessageRole.System).ToList();
        var systemTotal = this.Sum(sysMsgs);

        var adjustedBudget = new ContextBudget(
            this._budget.MaxTokens,
            this._budget.CompactionThreshold,
            this._budget.EmergencyThreshold,
            this._budget.ReservedTokens + systemTotal
        );

        var compacted = this._strategy.Compact(compactableMessages, adjustedBudget, this._counter);

        var result = sysMsgs.Count == 0 ? compacted : sysMsgs.Concat(compacted).ToList();

        this._lastPreparedTotal = this.Sum(result) + this._anchorCorrection;

        return result;
    }

    private int Sum(IReadOnlyList<Message> messages) => messages.Sum(this.EnsureCounted);

    private int EnsureCounted(Message message)
    {
        if (message.TokenCount is { } count)
            return count;

        var computed = this._counter.Count(message);
        message.TokenCount = computed;
        return computed;
    }

    private void ApplyAnchor(int? providerInputTokens)
    {
        if (providerInputTokens.HasValue)
            this._anchorCorrection = providerInputTokens.Value - this._lastPreparedTotal;
    }
}