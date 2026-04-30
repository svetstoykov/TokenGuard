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
    private readonly List<ContextMessage> _history = [];

    // Token total of the list most recently returned by PrepareAsync — used to compute anchor corrections.
    private int _lastEstimatedTotalTokens;

    // Additive correction applied to every raw estimate to account for systematic estimator drift.
    // Updated each time RecordModelResponse is called with a providerInputTokens value.
    private int _anchorCorrection;

    private int _pinnedTokenTotal;

    // Turn counter incremented on each PrepareAsync call where the history has changed since the last prepare.
    // Messages stamped with the same turn number form an atomic drop unit during emergency truncation.
    private int _currentTurn;
    private int _historyVersion;
    private int _lastPreparedVersion;

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
    /// A task that resolves to a <see cref="PrepareResult"/> containing the prepared messages
    /// and metadata describing what happened during preparation.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is the main read operation of the context. Await it immediately before every provider
    /// request. Use <see cref="PrepareResult.Messages"/> for the list that should be sent to the model.
    /// </para>
    /// <para>
    /// If the estimated token total is below the compaction trigger, the current history is
    /// returned directly with <see cref="PrepareResult.Outcome"/> set to <see cref="Enums.PrepareOutcome.Ready"/>.
    /// If the trigger is reached, the configured <see cref="ICompactionStrategy"/>
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
    public async Task<PrepareResult> PrepareAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(this._disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (this._historyVersion != this._lastPreparedVersion)
        {
            this._currentTurn++;
            this._lastPreparedVersion = this._historyVersion;
        }

        if (this._pinnedTokenTotal > this._budget.MaxTokens)
        {
            throw new PinnedTokenBudgetExceededException(this._pinnedTokenTotal, this._budget.MaxTokens);
        }

        IReadOnlyList<ContextMessage> messages = this._history;
        var totalBeforeCompaction = this.Sum(messages) + this._anchorCorrection;

        if (totalBeforeCompaction < this._budget.CompactionTriggerTokens)
        {
            this._lastEstimatedTotalTokens = totalBeforeCompaction;
            return new PrepareResult(
                messages,
                PrepareOutcome.Ready,
                totalBeforeCompaction,
                totalBeforeCompaction,
                messagesCompacted: 0);
        }

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

        var availableTokens = this._budget.MaxTokens - this._pinnedTokenTotal;

        var compacted = await this._strategy.CompactAsync(compactable, availableTokens, cancellationToken);

        var prepared = pinnedSlots.Count == 0
            ? compacted.Messages
            : this.ReassemblePreparedMessages(messages.Count, pinnedSlots, compacted.Messages);

        var preparedTotal = this.Sum(prepared) + this._anchorCorrection;

        var emergencyApplied = this.TryApplyEmergencyTruncation(prepared, preparedTotal, out var truncated);
        var final = emergencyApplied ? truncated! : prepared;
        var estimatedFinalTokens = this.Sum(final);
        var finalTokens = estimatedFinalTokens + this._anchorCorrection;
        var emergencyMessagesDropped = emergencyApplied ? prepared.Count - final.Count : 0;
        var messagesCompacted = compacted.MessagesAffected + emergencyMessagesDropped;

        this._lastEstimatedTotalTokens = estimatedFinalTokens;
        this._anchorCorrection = 0;

        var outcome = this.DetermineOutcome(finalTokens, messagesCompacted);
        var degradationReason = outcome is PrepareOutcome.Degraded or PrepareOutcome.ContextExhausted
            ? this.BuildDegradationReason(outcome, finalTokens, messagesCompacted)
            : null;

        return new PrepareResult(
            final,
            outcome,
            totalBeforeCompaction,
            finalTokens,
            messagesCompacted,
            degradationReason,
            emergencyMessagesDropped);
    }

    /// <summary>
    /// Maps the prepared token total and compaction activity to the final preparation outcome.
    /// </summary>
    /// <param name="finalTokens">The final token total after compaction and any emergency truncation.</param>
    /// <param name="messagesCompacted">The number of messages affected during preparation.</param>
    /// <returns>The caller-facing outcome for the current prepared payload.</returns>
    private PrepareOutcome DetermineOutcome(int finalTokens, int messagesCompacted)
    {
        if (finalTokens <= (long)this._budget.MaxTokens + this._budget.OverrunToleranceTokens)
        {
            return messagesCompacted > 0 ? PrepareOutcome.Compacted : PrepareOutcome.Ready;
        }

        return messagesCompacted == 0
            ? PrepareOutcome.ContextExhausted
            : PrepareOutcome.Degraded;
    }

    /// <summary>
    /// Builds the diagnostic message returned for degraded and exhausted outcomes.
    /// </summary>
    /// <param name="outcome">The outcome that requires a diagnostic explanation.</param>
    /// <param name="finalTokens">The final token total after preparation.</param>
    /// <param name="messagesCompacted">The number of messages affected during preparation.</param>
    /// <returns>A stable diagnostic string describing why the prepared payload is still over budget.</returns>
    private string BuildDegradationReason(PrepareOutcome outcome, int finalTokens, int messagesCompacted)
    {
        var effectiveMax = (long)this._budget.MaxTokens + this._budget.OverrunToleranceTokens;
        return outcome == PrepareOutcome.ContextExhausted
            ? $"A single message or structural content exceeds the budget ({finalTokens} tokens > {effectiveMax} max). Compaction is impossible."
            : $"Compaction reduced content but still exceeds budget ({finalTokens} tokens > {effectiveMax} max). {messagesCompacted} messages were compacted but insufficient.";
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
    /// The method returns immediately with no-op when <see cref="ContextBudget.EmergencyThreshold"/> is
    /// <see langword="null"/>, because emergency truncation is opt-in. Configure it on the budget only when
    /// hard message-dropping under extreme token pressure is acceptable for the target use case.
    /// </para>
    /// <para>
    /// When enabled, the method identifies eligible drop candidates by excluding pinned messages, which are never
    /// removed. The newest unpinned message is always preserved as an irreducible floor, so the returned list is never
    /// empty, and the latest active turn remains visible to the model.
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
    /// the reduced list; <see langword="false"/> when emergency truncation is disabled, when
    /// <paramref name="prepared"/> is already within budget, or when no eligible candidates exist.
    /// </returns>
    private bool TryApplyEmergencyTruncation(
        IReadOnlyList<ContextMessage> prepared,
        int currentTotal,
        out IReadOnlyList<ContextMessage>? truncated)
    {
        if (!this._budget.EmergencyTriggerTokens.HasValue)
        {
            truncated = null;
            return false;
        }

        var emergencyLimit = this._budget.EmergencyTriggerTokens.Value;

        if (currentTotal <= emergencyLimit)
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

        // Collect drop candidates as atomic turn groups (model message + its tool results) so
        // a tool result is never left without its preceding model turn, which would produce an
        // invalid conversation structure rejected by providers.
        var turnGroups = BuildTurnGroups(prepared, preservedFloorStartIndex);

        if (turnGroups.Count == 0)
        {
            truncated = null;
            return false;
        }

        // Drop whole turn groups oldest-first until the budget is satisfied or groups are exhausted.
        var dropIndices = new HashSet<int>();
        var total = currentTotal;
        foreach (var (groupIndices, groupTokens) in turnGroups)
        {
            if (total <= emergencyLimit)
                break;

            foreach (var idx in groupIndices)
                dropIndices.Add(idx);

            total -= groupTokens;
        }

        if (dropIndices.Count == 0)
        {
            truncated = null;
            return false;
        }

        truncated = prepared.Where((_, i) => !dropIndices.Contains(i)).ToList();
        return true;
    }

    /// <summary>
    /// Groups eligible prepared messages into atomic turn units for emergency truncation.
    /// </summary>
    /// <param name="prepared">The prepared message list produced for the next provider call.</param>
    /// <param name="limit">The exclusive upper bound for indices that may be considered for dropping.</param>
    /// <returns>Turn-ordered message groups paired with their aggregate token counts.</returns>
    private static IReadOnlyList<(IReadOnlyList<int> Indices, int Tokens)> BuildTurnGroups(IReadOnlyList<ContextMessage> prepared, int limit)
    {
        var groups = new List<(IReadOnlyList<int> Indices, int Tokens)>();
        var i = 0;

        while (i < limit)
        {
            var msg = prepared[i];
            if (msg.IsPinned)
            {
                i++;
                continue;
            }

            var turn = msg.Turn;
            var messageIndexes = new List<int> { i };
            var messageGroupTokens = msg.TokenCount ?? 0;
            i++;

            while (i < limit && !prepared[i].IsPinned && prepared[i].Turn == turn)
            {
                messageIndexes.Add(i);
                messageGroupTokens += prepared[i].TokenCount ?? 0;
                i++;
            }

            groups.Add((messageIndexes, messageGroupTokens));
        }

        return groups;
    }

    /// <summary>
    /// Finds the first index of the newest tail that emergency truncation must preserve.
    /// </summary>
    /// <param name="prepared">The prepared message list produced for the next provider call.</param>
    /// <returns>
    /// The inclusive start index of the preserved tail, or <c>prepared.Count</c> when every message is pinned.
    /// </returns>
    private int FindPreservedFloorStartIndex(IReadOnlyList<ContextMessage> prepared)
    {
        for (var i = 0; i < prepared.Count; i++)
        {
            if (prepared[i].State == CompactionState.Summarized)
            {
                return i;
            }
        }

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

    /// <summary>
    /// Sums message token counts while populating any missing cached counts.
    /// </summary>
    /// <param name="messages">The messages whose token counts should be aggregated.</param>
    /// <returns>The total token count across <paramref name="messages"/>.</returns>
    private int Sum(IReadOnlyList<ContextMessage> messages) => messages.Sum(this.EnsureCounted);

    /// <summary>
    /// Inserts a message into history, assigns its turn, and updates pinned token bookkeeping.
    /// </summary>
    /// <param name="message">The message to record.</param>
    /// <param name="index">
    /// The optional insertion index. When omitted, the message is appended to the end of history.
    /// </param>
    private void AddMessage(ContextMessage message, int? index = null)
    {
        message.Turn = this._currentTurn;

        if (index.HasValue)
        {
            this._history.Insert(index.Value, message);
        }
        else
        {
            this._history.Add(message);
        }

        this._historyVersion++;

        var tokenCount = this.EnsureCounted(message);

        if (message.IsPinned)
        {
            this._pinnedTokenTotal += tokenCount;
        }
    }

    /// <summary>
    /// Replaces one history entry while keeping pinned token bookkeeping consistent.
    /// </summary>
    /// <param name="index">The history index to replace.</param>
    /// <param name="message">The replacement message.</param>
    private void ReplaceMessage(int index, ContextMessage message)
    {
        var existing = this._history[index];
        if (existing.IsPinned)
        {
            this._pinnedTokenTotal -= this.EnsureCounted(existing);
        }

        this._history[index] = message;
        this._historyVersion++;

        var tokenCount = this.EnsureCounted(message);
        if (message.IsPinned)
        {
            this._pinnedTokenTotal += tokenCount;
        }
    }

    /// <summary>
    /// Reassembles pinned messages into the compacted stream at their original positions.
    /// </summary>
    /// <param name="totalCount">The final total number of prepared messages after reassembly.</param>
    /// <param name="pinnedSlots">The pinned messages paired with their original indices.</param>
    /// <param name="compactedMessages">The compacted unpinned message stream.</param>
    /// <returns>A prepared list that preserves the original placement of pinned messages.</returns>
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

    /// <summary>
    /// Returns the cached token count for a message or computes and stores it on first use.
    /// </summary>
    /// <param name="contextMessage">The message whose token count should be ensured.</param>
    /// <returns>The cached or newly computed token count for <paramref name="contextMessage"/>.</returns>
    private int EnsureCounted(ContextMessage contextMessage)
    {
        if (contextMessage.TokenCount is { } count)
            return count;

        var computed = this._counter.Count(contextMessage);
        contextMessage.TokenCount = computed;
        return computed;
    }

    /// <summary>
    /// Recomputes the active anchor correction from provider-reported input tokens.
    /// </summary>
    /// <param name="providerInputTokens">
    /// The exact provider-reported input token total for the most recently prepared payload.
    /// </param>
    private void ApplyAnchor(int? providerInputTokens)
    {
        if (providerInputTokens.HasValue)
            this._anchorCorrection = providerInputTokens.Value - this._lastEstimatedTotalTokens;
    }
}
