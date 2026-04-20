using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Exceptions;
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
/// Messages may be pinned so they survive compaction and emergency truncation unchanged at their original recorded
/// position. System prompts use this behavior by default so active instructions remain durable without requiring a
/// separate role-based preservation path.
/// </para>
/// <para>
/// Token counts are estimated through the configured <see cref="ITokenCounter"/> and cached on
/// each recorded <see cref="ContextMessage"/>. If the provider later reports an exact input token count,
/// that value can be supplied through <see cref="RecordModelResponse(IEnumerable{ContentSegment}, int?)"/>
/// so future preparation stays better aligned with the provider's own counting behavior.
/// </para>
/// </remarks>
public sealed class ConversationContext : IConversationContext
{
    private readonly ContextBudget _budget;
    private readonly ITokenCounter _counter;
    private readonly ICompactionStrategy _strategy;
    private readonly ICompactionObserver? _observer;

    private readonly List<ContextMessage> _history = [];

    // Token total of the list most recently returned by PrepareAsync — used to compute anchor corrections.
    private int _lastPreparedTotal;

    // Additive correction applied to every raw estimate to account for systematic estimator drift.
    // Updated each time RecordModelResponse is called with a providerInputTokens value.
    private int _anchorCorrection;

    private int _pinnedTokenTotal;

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
    /// as possible, so compaction decisions are based on realistic estimates.
    /// </param>
    /// <param name="strategy">
    /// Produces a smaller message list when the current history no longer fits comfortably within
    /// the configured budget.
    /// </param>
    /// <param name="observer">
    /// An optional observer notified after each compaction cycle that modifies the history.
    /// When <see langword="null"/>, no compaction notifications are emitted.
    /// </param>
    public ConversationContext(ContextBudget budget, ITokenCounter counter, ICompactionStrategy strategy, ICompactionObserver? observer = null)
    {
        this._budget = budget;
        this._counter = counter;
        this._strategy = strategy;
        this._observer = observer;
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
    /// This property is useful for inspection, testing, logging, and debugging. It is different from
    ///  the request payload returned by <see cref="PrepareAsync(CancellationToken)"/>, which may be compacted.
    /// </para>
    /// </remarks>
    public IReadOnlyList<ContextMessage> History
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
    /// The system message is kept at the start of the history and is recorded as pinned so later preparation never masks,
    /// summarizes, or drops it.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="text"/> is null or whitespace.</exception>
    public void SetSystemPrompt(string text)
    {
        ObjectDisposedException.ThrowIf(this._disposed, this);
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("System prompt text cannot be null or whitespace.", nameof(text));

        var message = ContextMessage.FromText(MessageRole.System, text) with { IsPinned = true };

        var existing = this._history.FindIndex(m => m.Role == MessageRole.System);
        if (existing >= 0)
        {
            this.ReplaceMessage(existing, message);
            return;
        }

        this.AddMessage(message, 0);
    }

    /// <summary>
    /// Appends a pinned message to the conversation history.
    /// </summary>
    /// <param name="role">The participant role that produced the message.</param>
    /// <param name="text">The plain-text payload to record.</param>
    /// <remarks>
    /// Pinned messages remain at their recorded position and are excluded from compaction and emergency truncation.
    /// Use this API for durable constraints or instructions that should survive regardless of where they appear.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="text"/> is null or whitespace.</exception>
    public void AddPinnedMessage(MessageRole role, string text)
    {
        ObjectDisposedException.ThrowIf(this._disposed, this);
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Pinned message text cannot be null or whitespace.", nameof(text));

        var message = ContextMessage.FromText(role, text) with { IsPinned = true };
        this.AddMessage(message);
    }

    /// <summary>
    /// Appends a pinned multi-segment message to the conversation history.
    /// </summary>
    /// <param name="role">The participant role that produced the message.</param>
    /// <param name="content">The ordered content segments that make up the pinned message payload.</param>
    /// <remarks>
    /// This overload preserves multi-segment message structure while still ensuring the recorded message is never masked
    /// or dropped during later preparation.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="content"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="content"/> contains no segments.</exception>
    public void AddPinnedMessage(MessageRole role, IEnumerable<ContentSegment> content)
    {
        ObjectDisposedException.ThrowIf(this._disposed, this);
        ArgumentNullException.ThrowIfNull(content);

        var segments = content.ToArray();
        if (segments.Length == 0)
            throw new ArgumentException("Pinned message content must contain at least one segment.", nameof(content));

        var message = new ContextMessage
        {
            Role = role,
            Segments = segments,
            IsPinned = true,
        };

        this.AddMessage(message);
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

        var message = ContextMessage.FromText(MessageRole.User, text);
        this.AddMessage(message);
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

        var message = new ContextMessage { Role = MessageRole.Model, Segments = segments };
        this.AddMessage(message);
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

        var message = ContextMessage.FromContent(MessageRole.Tool, new ToolResultContent(toolCallId, toolName, content));
        this.AddMessage(message);
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
    /// Pinned messages are handled specially. They are excluded from the compactable set, their token cost is added to
    /// the reserved budget passed into the compaction strategy, and they are reassembled into the final result at their
    /// original positions after compaction finishes.
    /// </para>
    /// <para>
    /// Calling this method does not modify <see cref="History"/>. It only determines what subset or
    /// representation of that history should be sent next.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<ContextMessage>> PrepareAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(this._disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (this._pinnedTokenTotal > this._budget.EmergencyTriggerTokens)
        {
            throw new PinnedTokenBudgetExceededException(this._pinnedTokenTotal, this._budget.EmergencyTriggerTokens);
        }

        IReadOnlyList<ContextMessage> messages = this._history;
        var total = this.Sum(messages) + this._anchorCorrection;

        if (total < this._budget.CompactionTriggerTokens)
        {
            this._lastPreparedTotal = total;
            return messages;
        }

        var trigger = total >= this._budget.EmergencyTriggerTokens
            ? CompactionTrigger.Emergency
            : CompactionTrigger.Normal;

        var pinnedSlots = new List<(int Index, ContextMessage Message)>();
        List<ContextMessage>? compactableMessages = null;

        for (var i = 0; i < messages.Count; i++)
        {
            var message = messages[i];
            if (message.IsPinned)
            {
                pinnedSlots.Add((i, message));
                continue;
            }

            compactableMessages ??= [];
            compactableMessages.Add(message);
        }

        IReadOnlyList<ContextMessage> compactable = compactableMessages ?? [];

        var adjustedBudget = new ContextBudget(
            this._budget.MaxTokens,
            this._budget.CompactionThreshold,
            this._budget.EmergencyThreshold,
            this._budget.ReservedTokens + this._pinnedTokenTotal
        );

        var compacted = await this._strategy.CompactAsync(compactable, adjustedBudget, this._counter, cancellationToken);

        var prepared = pinnedSlots.Count == 0
            ? compacted.Messages
            : this.ReassemblePreparedMessages(messages.Count, pinnedSlots, compacted.Messages);

        var preparedTotal = this.Sum(prepared) + this._anchorCorrection;
        var emergencyApplied = this.TryApplyEmergencyTruncation(prepared, preparedTotal, out var truncated);

        var final = emergencyApplied ? truncated! : prepared;
        var finalTokens = this.Sum(final);

        if (emergencyApplied)
        {
            var droppedCount = prepared.Count - final.Count;
            var mergedResult = new CompactionResult(
                final,
                compacted.TokensBefore,
                finalTokens,
                compacted.MessagesAffected + droppedCount,
                compacted.StrategyName,
                WasApplied: true);
            this._observer?.OnCompaction(new CompactionEvent(mergedResult, DateTimeOffset.UtcNow, CompactionTrigger.Emergency, this._budget));
        }
        else if (compacted.WasApplied)
        {
            this._observer?.OnCompaction(new CompactionEvent(compacted, DateTimeOffset.UtcNow, trigger, this._budget));
        }

        this._lastPreparedTotal = finalTokens;
        this._anchorCorrection = 0;

        return final;
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

    /// <summary>
    /// Applies an oldest-first emergency truncation pass to <paramref name="prepared"/> when the
    /// current token total still exceeds <see cref="ContextBudget.EmergencyTriggerTokens"/> after
    /// the primary compaction strategy has run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The method identifies eligible drop candidates by excluding pinned messages, which are never removed. The newest
    /// unpinned message is always preserved as an irreducible floor, so the returned list is never empty, and the latest
    /// active turn remains visible to the model.
    /// </para>
    /// <para>
    /// Candidates are removed oldest first. The loop stops as soon as the running token total
    /// reaches or falls below <see cref="ContextBudget.EmergencyTriggerTokens"/>, or when the
    /// remaining list has reached its safety floor and no further eligible candidates remain. If
    /// that preserved floor itself still exceeds the emergency threshold, the method returns the
    /// over-budget floor unchanged, because retaining the newest indispensable conversation tail is
    /// more important than forcing a fit-to-budget outcome.
    /// </para>
    /// </remarks>
    /// <param name="prepared">The assembled message list produced after the primary strategy pass.</param>
    /// <param name="currentTotal">
    /// The current token total of <paramref name="prepared"/>, including any active anchor correction.
    /// </param>
    /// <param name="truncated">
    /// When this method returns <see langword="true"/>, contains a new list with the oldest eligible
    /// messages removed. When this method returns <see langword="false"/>, this value is
    /// <see langword="null"/> and <paramref name="prepared"/> should be used as-is.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when truncation was applied and <paramref name="truncated"/> contains
    /// the reduced list; <see langword="false"/> when <paramref name="prepared"/> is already within
    /// budget or no eligible candidates exist.
    /// </returns>
    private bool TryApplyEmergencyTruncation(IReadOnlyList<ContextMessage> prepared, int currentTotal, out IReadOnlyList<ContextMessage>? truncated)
    {
        if (currentTotal <= this._budget.EmergencyTriggerTokens)
        {
            truncated = null;
            return false;
        }

        var preservedFloorStartIndex = this.FindPreservedFloorStartIndex(prepared);

        // Nothing to drop when the prepared list already consists only of the preserved floor.
        if (preservedFloorStartIndex <= 0)
        {
            truncated = null;
            return false;
        }

        // Collect drop candidates: unpinned messages that appear before the preserved floor,
        // oldest first.
        var candidates = new List<(int Index, int Tokens)>();
        for (var i = 0; i < preservedFloorStartIndex; i++)
        {
            var msg = prepared[i];
            if (msg.IsPinned)
                continue;

            candidates.Add((i, msg.TokenCount ?? 0));
        }

        if (candidates.Count == 0)
        {
            truncated = null;
            return false;
        }

        // Drop oldest-first until the budget is satisfied or candidates are exhausted.
        var dropIndices = new HashSet<int>();
        var total = currentTotal;
        foreach (var (index, tokens) in candidates)
        {
            if (total <= this._budget.EmergencyTriggerTokens)
                break;

            dropIndices.Add(index);
            total -= tokens;
        }

        if (dropIndices.Count == 0)
        {
            truncated = null;
            return false;
        }

        truncated = prepared.Where((_, i) => !dropIndices.Contains(i)).ToList();
        return true;
    }

    private int FindPreservedFloorStartIndex(IReadOnlyList<ContextMessage> prepared)
    {
        var newestUnpinnedIndex = -1;
        for (var i = prepared.Count - 1; i >= 0; i--)
        {
            if (!prepared[i].IsPinned)
            {
                // The preserved tail starts from the newest unpinned message by default.
                newestUnpinnedIndex = i;
                break;
            }
        }

        if (newestUnpinnedIndex < 0)
            return prepared.Count;

        var floorStartIndex = newestUnpinnedIndex;
        
        // If last message was not a Model message, we should just return it.
        if (prepared[newestUnpinnedIndex].Role != MessageRole.Model) return floorStartIndex;
        
        // Otherwise the conversation ends with a model reply, preserve the triggering user turn too.
        for (var i = newestUnpinnedIndex - 1; i >= 0; i--)
        {
            if (prepared[i].IsPinned)
                continue;

            if (prepared[i].Role == MessageRole.User)
                floorStartIndex = i;

            break;
        }

        return floorStartIndex;
    }

    private int Sum(IReadOnlyList<ContextMessage> messages) => messages.Sum(this.EnsureCounted);

    private void AddMessage(ContextMessage message, int? index = null)
    {
        if (index.HasValue)
        {
            this._history.Insert(index.Value, message);
        }
        else
        {
            this._history.Add(message);
        }

        var tokenCount = this.EnsureCounted(message);

        if (message.IsPinned)
        {
            this._pinnedTokenTotal += tokenCount;
        }
    }

    private void ReplaceMessage(int index, ContextMessage message)
    {
        var existing = this._history[index];
        if (existing.IsPinned)
        {
            this._pinnedTokenTotal -= this.EnsureCounted(existing);
        }

        this._history[index] = message;

        var tokenCount = this.EnsureCounted(message);
        if (message.IsPinned)
        {
            this._pinnedTokenTotal += tokenCount;
        }
    }

    private List<ContextMessage> ReassemblePreparedMessages(
        int totalCount,
        IReadOnlyList<(int Index, ContextMessage Message)> pinnedSlots,
        IReadOnlyList<ContextMessage> compactedMessages)
    {
        var prepared = new List<ContextMessage>(totalCount);
        var pinnedIndex = 0;
        var compactedIndex = 0;

        for (var i = 0; i < totalCount; i++)
        {
            if (pinnedIndex < pinnedSlots.Count && pinnedSlots[pinnedIndex].Index == i)
            {
                prepared.Add(pinnedSlots[pinnedIndex].Message);
                pinnedIndex++;
                continue;
            }

            prepared.Add(compactedMessages[compactedIndex]);
            compactedIndex++;
        }

        return prepared;
    }

    private int EnsureCounted(ContextMessage contextMessage)
    {
        if (contextMessage.TokenCount is { } count)
            return count;

        var computed = this._counter.Count(contextMessage);
        contextMessage.TokenCount = computed;
        return computed;
    }

    private void ApplyAnchor(int? providerInputTokens)
    {
        if (providerInputTokens.HasValue)
            this._anchorCorrection = providerInputTokens.Value - this._lastPreparedTotal;
    }
}
