using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;

namespace TokenGuard.Core;

/// <summary>
/// Represents the state of one LLM conversation. It records the full message history and prepares
/// the next request payload so callers can keep sending a conversation forward without manually
/// managing token limits, system prompts, tool results, or compaction behavior.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="ConversationContext"/> acts as the central state container for an agent loop or
/// chat session. Messages are added to the history as user input, model responses, and tool
/// results occur, and <see cref="PrepareAsync(CancellationToken)"/> returns the message list that should be sent to the
/// provider for the next request.
/// </para>
/// <para>
/// The context keeps the original recorded history intact. When the conversation is still within
/// budget, <see cref="PrepareAsync(CancellationToken)"/> returns that history directly. When the configured compaction
/// trigger is reached, the context delegates to the configured <see cref="ICompactionStrategy"/>
/// to produce a smaller request payload while preserving the overall conversation flow.
/// </para>
/// <para>
/// System messages are treated specially. The context stores at most one system message, keeps it
/// at the front of the history, and preserves it across compaction so the model continues to see
/// the active instructions for the conversation.
/// </para>
/// <para>
/// Token counts are estimated through the configured <see cref="ITokenCounter"/> and cached on
/// each recorded <see cref="SemanticMessage"/>. If the provider later reports an exact input token count,
/// that value can be supplied through <see cref="RecordModelResponse(IEnumerable{ContentSegment}, int?)"/>
/// so future preparation stays better aligned with the provider's own counting behavior.
/// </para>
/// </remarks>
public sealed class ConversationContext : IDisposable
{
    private readonly ContextBudget _budget;
    private readonly ITokenCounter _counter;
    private readonly ICompactionStrategy _strategy;

    private readonly List<SemanticMessage> _history = [];

    // Token total of the list most recently returned by PrepareAsync — used to compute anchor corrections.
    private int _lastPreparedTotal;

    // Additive correction applied to every raw estimate to account for systematic estimator drift.
    // Updated each time RecordModelResponse is called with a providerInputTokens value.
    private int _anchorCorrection;

    private bool _disposed;

    /// <summary>
    /// Creates a conversation context with the budget, token counter, and compaction strategy
    /// that define how requests are prepared.
    /// </summary>
    /// <param name="budget">
    /// Defines the token limits for the conversation, including when compaction starts and how
    /// many tokens should remain reserved for the next response.
    /// </param>
    /// <param name="counter">
    /// Counts tokens for individual messages. This should match the target provider as closely
    /// as possible so compaction decisions are based on realistic estimates.
    /// </param>
    /// <param name="strategy">
    /// Produces a smaller message list when the current history no longer fits comfortably within
    /// the configured budget.
    /// </param>
    public ConversationContext(ContextBudget budget, ITokenCounter counter, ICompactionStrategy strategy)
    {
        this._budget = budget;
        this._counter = counter;
        this._strategy = strategy;
    }

    /// <summary>
    /// Gets the full conversation history exactly as it has been recorded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a live, read-only view of the internal history. New messages recorded through this
    /// instance appear here immediately.
    /// </para>
    /// <para>
    /// This property is useful for inspection, testing, logging, and debugging. It is not the
    /// same as the request payload returned by <see cref="PrepareAsync(CancellationToken)"/>, which may be compacted.
    /// </para>
    /// </remarks>
    public IReadOnlyList<SemanticMessage> History
    {
        get
        {
            ObjectDisposedException.ThrowIf(this._disposed, this);
            return this._history;
        }
    }

    /// <summary>
    /// Sets the system message for the conversation.
    /// </summary>
    /// <param name="text">The system prompt text.</param>
    /// <remarks>
    /// <para>
    /// A conversation context stores at most one system message. If a system message already
    /// exists, this method replaces it in place instead of appending a second one.
    /// </para>
    /// <para>
    /// The system message is kept at the start of the history and is preserved separately during
    /// compaction so it remains part of prepared requests.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="text"/> is null or whitespace.</exception>
    public void SetSystemPrompt(string text)
    {
        ObjectDisposedException.ThrowIf(this._disposed, this);
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("System prompt text cannot be null or whitespace.", nameof(text));

        var message = SemanticMessage.FromText(MessageRole.System, text);

        var existing = this._history.FindIndex(m => m.Role == MessageRole.System);
        if (existing >= 0)
            this._history[existing] = message;
        else
            this._history.Insert(0, message);

        this.EnsureCounted(message);
    }

    /// <summary>
    /// Appends a user message to the conversation history.
    /// </summary>
    /// <param name="text">The user message text.</param>
    /// <remarks>
    /// This method records the message exactly as a new user turn. It does not trigger
    /// compaction. Compaction is evaluated only when <see cref="PrepareAsync(CancellationToken)"/> is called.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="text"/> is null or whitespace.</exception>
    public void AddUserMessage(string text)
    {
        ObjectDisposedException.ThrowIf(this._disposed, this);
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("User message text cannot be null or whitespace.", nameof(text));

        var message = SemanticMessage.FromText(MessageRole.User, text);
        this._history.Add(message);
        this.EnsureCounted(message);
    }

    /// <summary>
    /// Records the model response as the next message in the conversation history.
    /// </summary>
    /// <param name="content">
    /// The content segments returned by the model. This can contain plain text, tool-use requests,
    /// or any other supported content segments for a model message.
    /// </param>
    /// <param name="providerInputTokens">
    /// The exact input token count reported by the provider for the request that produced this
    /// response. When supplied, this value is used to correct future token estimates so
    /// <see cref="PrepareAsync(CancellationToken)"/> can stay aligned with the provider's counting behavior.
    /// </param>
    /// <remarks>
    /// <para>
    /// Call this after a model response is received. The response is stored as a single
    /// <see cref="MessageRole.Model"/> message, even when it contains multiple content segments.
    /// </para>
    /// <para>
    /// If <paramref name="providerInputTokens"/> is provided, the context compares the provider's
    /// exact input token count with its most recent prepared estimate and stores the difference as
    /// a correction factor. That correction is applied to later <see cref="PrepareAsync(CancellationToken)"/> calls until
    /// the next compaction cycle resets it.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="content"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="content"/> is empty.</exception>
    public void RecordModelResponse(IEnumerable<ContentSegment> content, int? providerInputTokens = null)
    {
        ObjectDisposedException.ThrowIf(this._disposed, this);
        ArgumentNullException.ThrowIfNull(content);

        var segments = content.ToArray();
        if (segments.Length == 0)
            throw new ArgumentException("Content must contain at least one segment.", nameof(content));

        var message = new SemanticMessage { Role = MessageRole.Model, Content = segments };
        this._history.Add(message);
        this.EnsureCounted(message);
        this.ApplyAnchor(providerInputTokens);
    }
    
    /// <summary>
    /// Records the result of one tool execution.
    /// </summary>
    /// <param name="toolCallId">The tool call identifier this result corresponds to.</param>
    /// <param name="toolName">The name of the tool that produced this result.</param>
    /// <param name="content">The tool output payload.</param>
    /// <remarks>
    /// <para>
    /// Call this once for each tool invocation requested by the model. The result is stored as a
    /// single <see cref="MessageRole.Tool"/> message so later requests preserve the tool call chain.
    /// </para>
    /// <para>
    /// This method only records the result. It does not validate whether the tool call identifier
    /// matches a previously recorded <see cref="ToolUseContent"/> segment.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="toolCallId"/>, <paramref name="toolName"/>, or
    /// <paramref name="content"/> is null or whitespace.
    /// </exception>
    public void RecordToolResult(string toolCallId, string toolName, string content)
    {
        ObjectDisposedException.ThrowIf(this._disposed, this);
        if (string.IsNullOrWhiteSpace(toolCallId))
            throw new ArgumentException("Tool call id cannot be null or whitespace.", nameof(toolCallId));

        if (string.IsNullOrWhiteSpace(toolName))
            throw new ArgumentException("Tool name cannot be null or whitespace.", nameof(toolName));

        ArgumentNullException.ThrowIfNull(content);

        var message = SemanticMessage.FromContent(MessageRole.Tool, new ToolResultContent(toolCallId, toolName, content));
        this._history.Add(message);
        this.EnsureCounted(message);
    }

    /// <summary>
    /// Builds the message list to send for the next LLM request.
    /// </summary>
    /// <param name="cancellationToken">A token that can cancel asynchronous compaction before the prepared list is produced.</param>
    /// <returns>
    /// A task that resolves to the full history when it fits within the configured budget, or to a
    /// compacted message list when compaction is required.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is the main read operation of the context. Await it immediately before every provider
    /// request. The returned list is the list that should be sent to the model.
    /// </para>
    /// <para>
    /// If the estimated token total is below the compaction trigger, the current history is
    /// returned directly. If the trigger is reached, the configured <see cref="ICompactionStrategy"/>
    /// is awaited to produce a smaller list.
    /// </para>
    /// <para>
    /// System messages are handled specially. They are separated from the rest of the history,
    /// excluded from the compactable set, and then reattached to the front of the final result.
    /// Their token cost is added to the reserved budget passed into the compaction strategy so the
    /// strategy does not accidentally consume space that the system message already needs.
    /// </para>
    /// <para>
    /// Calling this method does not modify <see cref="History"/>. It only determines what subset or
    /// representation of that history should be sent next.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<SemanticMessage>> PrepareAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(this._disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        var messages = (IReadOnlyList<SemanticMessage>)this._history;
        var total = this.Sum(messages) + this._anchorCorrection;

        if (total < this._budget.CompactionTriggerTokens)
        {
            this._lastPreparedTotal = total;
            return messages;
        }

        var systemMessages = messages.Where(m => m.Role == MessageRole.System).ToList();
        var compactableMessages = systemMessages.Count == 0 ? messages : messages.Where(m => m.Role != MessageRole.System).ToList();
        var systemTotal = this.Sum(systemMessages);

        var adjustedBudget = new ContextBudget(
            this._budget.MaxTokens,
            this._budget.CompactionThreshold,
            this._budget.EmergencyThreshold,
            this._budget.ReservedTokens + systemTotal
        );

        var compacted = await this._strategy.CompactAsync(compactableMessages, adjustedBudget, this._counter, cancellationToken);

        var result = systemMessages.Count == 0 ? compacted : systemMessages.Concat(compacted).ToList();

        this._lastPreparedTotal = this.Sum(result) + this._anchorCorrection;
        this._anchorCorrection = 0;

        return result;
    }

    /// <summary>
    /// Releases the conversation history held by this context.
    /// </summary>
    /// <remarks>
    /// After disposal, all public members throw <see cref="ObjectDisposedException"/>. A
    /// <see cref="ConversationContext"/> should be scoped to a single conversation and disposed
    /// when that conversation ends. Registering it as a singleton will cause the history to grow
    /// for the lifetime of the process and will not be released until the process exits.
    /// </remarks>
    public void Dispose()
    {
        if (this._disposed)
            return;

        this._disposed = true;
        this._history.Clear();
    }

    private int Sum(IReadOnlyList<SemanticMessage> messages) => messages.Sum(this.EnsureCounted);

    private int EnsureCounted(SemanticMessage semanticMessage)
    {
        if (semanticMessage.TokenCount is { } count)
            return count;

        var computed = this._counter.Count(semanticMessage);
        semanticMessage.TokenCount = computed;
        return computed;
    }

    private void ApplyAnchor(int? providerInputTokens)
    {
        if (providerInputTokens.HasValue)
            this._anchorCorrection = providerInputTokens.Value - this._lastPreparedTotal;
    }
}
