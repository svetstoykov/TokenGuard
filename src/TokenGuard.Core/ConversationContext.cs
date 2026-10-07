using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Diagnostics;
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
/// Messages may be pinned so they survive compaction and emergency truncation unchanged, between the messages they
/// were recorded between. System prompts use this behavior by default so active instructions remain durable without
/// requiring a separate role-based preservation path.
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
    private readonly TimedTokenCounter _counter;
    private readonly ICompactionStrategy _strategy;
    private readonly ConversationDiagnostics _diagnostics;
    private readonly ILogger _logger;
    private readonly ConversationHealth _health;
    private readonly List<ContextMessage> _history = [];

    // Token total of the list most recently returned by PrepareAsync — used to compute anchor corrections.
    private int _lastEstimatedTotalTokens;

    // Additive correction applied to every raw estimate to account for systematic estimator drift.
    // Updated each time RecordModelResponse is called with a providerInputTokens value.
    private int _anchorCorrection;

    private int _pinnedTokenTotal;

    // Turn counter incremented on each PrepareAsync call where the history has changed since the last prepare.
    // It labels logs, activities, and health signals; turn groups for truncation come from message roles.
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
    internal ConversationContext(ContextBudget budget, ITokenCounter counter, ICompactionStrategy strategy)
        : this(budget, counter, strategy, new ConversationDiagnostics(NullLoggerFactory.Instance, ConversationDiagnostics.DefaultContextName))
    {
    }

    /// <summary>
    /// Creates a conversation context that reports its activity through the supplied diagnostics.
    /// </summary>
    /// <param name="budget">Defines the token limits for the conversation.</param>
    /// <param name="counter">Counts tokens for individual messages.</param>
    /// <param name="strategy">Produces a smaller message list when the history reaches the compaction trigger.</param>
    /// <param name="diagnostics">The logger factory and identifiers this conversation reports with.</param>
    internal ConversationContext(ContextBudget budget, ITokenCounter counter, ICompactionStrategy strategy, ConversationDiagnostics diagnostics)
    {
        this._budget = budget;
        this._counter = counter as TimedTokenCounter ?? new TimedTokenCounter(counter);
        this._strategy = strategy;
        this._diagnostics = diagnostics;
        this._logger = diagnostics.LoggerFactory.CreateLogger<ConversationContext>();
        this._health = new ConversationHealth(this._logger, diagnostics);
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
    /// <para>
    /// Pinned messages are excluded from compaction and emergency truncation. Use this API for durable constraints or
    /// instructions that should survive regardless of where they appear.
    /// </para>
    /// <para>
    /// In every prepared payload a pinned message stays between the messages it was recorded between: after every
    /// surviving message recorded before it and before every surviving message recorded after it. When older messages
    /// are replaced by a summary, a pinned message recorded after any of them follows the summary, and the summary can
    /// describe messages recorded on both sides of it.
    /// </para>
    /// <para>
    /// A pinned message recorded between a model message that carries tool calls and the tool results that answer them
    /// is placed before that model message, so the exchange reaches the provider intact.
    /// </para>
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
    /// <para>
    /// This overload preserves multi-segment message structure while still ensuring the recorded message is never masked
    /// or dropped during later preparation.
    /// </para>
    /// <para>
    /// See the single-segment overload <see cref="AddPinnedMessage(MessageRole, string)"/> for where a pinned message
    /// appears in a prepared payload.
    /// </para>
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
    /// If the estimated token total is below the compaction trigger, a copy of the current history is
    /// returned with <see cref="PrepareResult.Outcome"/> set to <see cref="Enums.PrepareOutcome.Ready"/>.
    /// If the trigger is reached, the configured <see cref="ICompactionStrategy"/>
    /// is awaited to produce a smaller list.
    /// </para>
    /// <para>
    /// Pinned messages are handled specially. They are excluded from the compactable set, their token cost is added to
    /// the reserved budget passed into the compaction strategy, and they are put back between the surviving messages
    /// they were recorded between after compaction finishes. A pinned message recorded inside a tool exchange is placed
    /// before the model message that carries the tool calls, in every prepared payload.
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

        var conversationId = this._diagnostics.ConversationId;
        var contextName = this._diagnostics.ContextName;

        using var activity = TokenGuardTelemetry.ActivitySource.StartActivity(TokenGuardTelemetry.PrepareActivityName);
        activity?.SetTag(TokenGuardTelemetry.ConversationIdTag, conversationId)
            .SetTag(TokenGuardTelemetry.ContextNameTag, contextName)
            .SetTag(TokenGuardTelemetry.TurnTag, this._currentTurn)
            .SetTag(TokenGuardTelemetry.MaxTokensTag, this._budget.MaxTokens);

        this._health.OnPrepareStarted(this._pinnedTokenTotal, this._budget.MaxTokens);

        if (this._pinnedTokenTotal > this._budget.MaxTokens)
        {
            ConversationContextLog.PinnedBudgetExceeded(
                this._logger, conversationId, contextName, this._currentTurn, this._pinnedTokenTotal, this._budget.MaxTokens);
            activity?.SetStatus(ActivityStatusCode.Error, nameof(PinnedTokenBudgetExceededException));
            throw new PinnedTokenBudgetExceededException(this._pinnedTokenTotal, this._budget.MaxTokens);
        }

        var timePrepare = TokenGuardTelemetry.PrepareDuration.Enabled || this._logger.IsEnabled(LogLevel.Information);
        var startTimestamp = timePrepare ? Stopwatch.GetTimestamp() : 0;
        var timeCounting = TokenGuardTelemetry.TokenCountingDuration.Enabled;
        if (timeCounting)
            this._counter.StartTiming();

        IReadOnlyList<ContextMessage> messages = this._history;
        var totalBeforeCompaction = this.Sum(messages) + this._anchorCorrection;

        if (totalBeforeCompaction < this._budget.CompactionTriggerTokens)
        {
            ConversationContextLog.PrepareBelowTrigger(
                this._logger, conversationId, contextName, this._currentTurn, totalBeforeCompaction, this._budget.CompactionTriggerTokens,
                this._budget.MaxTokens);
            this.RecordPrepareTelemetry(
                activity, PrepareOutcome.Ready, totalBeforeCompaction, totalBeforeCompaction, messagesCompacted: 0, startTimestamp, timeCounting);
            this._health.OnPreparedBelowTrigger(totalBeforeCompaction);

            this._lastEstimatedTotalTokens = totalBeforeCompaction;
            return new PrepareResult(
                this.MovePinnedMessagesOutOfToolExchanges(messages),
                PrepareOutcome.Ready,
                totalBeforeCompaction,
                totalBeforeCompaction,
                messagesCompacted: 0);
        }

        var (pinnedSlots, compactableMessages) = SplitPinned(messages);
        IReadOnlyList<ContextMessage> compactable = compactableMessages;

        var availableTokens = this._budget.MaxTokens - this._pinnedTokenTotal;

        var compacted = await this.RunStrategyAsync(compactable, availableTokens, cancellationToken);

        var prepared = pinnedSlots.Count == 0
            ? compacted.Messages
            : ReassemblePreparedMessages(pinnedSlots, compactable, compacted.Messages);

        var preparedTotal = this.Sum(prepared) + this._anchorCorrection;

        var emergencyApplied = this.TryApplyEmergencyTruncation(prepared, preparedTotal, out var truncated);
        var final = emergencyApplied ? truncated! : prepared;
        var estimatedFinalTokens = this.Sum(final);
        var emergencyMessagesDropped = emergencyApplied ? prepared.Count - final.Count : 0;
        var messagesCompacted = compacted.MessagesAffected + emergencyMessagesDropped;

        this.LogPinnedPlacements(pinnedSlots, final);

        this._lastEstimatedTotalTokens = estimatedFinalTokens;
        this._anchorCorrection = 0;

        var outcome = this.DetermineOutcome(estimatedFinalTokens, messagesCompacted);
        var isOverBudget = outcome is PrepareOutcome.CompactionInsufficient or PrepareOutcome.CannotCompact;
        var budgetFailureReason = isOverBudget ? this.BuildBudgetFailureReason(outcome, estimatedFinalTokens, messagesCompacted) : null;

        if (this._logger.IsEnabled(LogLevel.Information))
        {
            ConversationContextLog.CompactionCompleted(
                this._logger, conversationId, contextName, this._currentTurn, outcome, totalBeforeCompaction, estimatedFinalTokens,
                messagesCompacted, emergencyMessagesDropped, compacted.StrategyName, Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
        }

        if (emergencyMessagesDropped > 0)
        {
            ConversationContextLog.EmergencyTruncationApplied(
                this._logger, conversationId, contextName, this._currentTurn, emergencyMessagesDropped, preparedTotal, estimatedFinalTokens);
            TokenGuardTelemetry.EmergencyTruncations.Add(1, this._diagnostics.ContextNameTag);
            activity?.AddEvent(new ActivityEvent(
                TokenGuardTelemetry.EmergencyTruncationEventName,
                tags: new ActivityTagsCollection { { TokenGuardTelemetry.MessagesDroppedTag, emergencyMessagesDropped } }));
        }

        if (compacted.SummarizationError is not null)
        {
            ConversationContextLog.SummarizationFailed(
                this._logger, compacted.SummarizationError, conversationId, contextName, this._currentTurn, compacted.StrategyName);
        }

        var effectiveMaxTokens = (long)this._budget.MaxTokens + this._budget.OverrunToleranceTokens;
        if (isOverBudget)
        {
            ConversationContextLog.PrepareOverBudget(
                this._logger, conversationId, contextName, this._currentTurn, outcome, estimatedFinalTokens, effectiveMaxTokens);
        }

        this._health.OnCompacted(
            this._currentTurn, totalBeforeCompaction, estimatedFinalTokens, compacted.SummarizationError, isOverBudget, effectiveMaxTokens,
            emergencyMessagesDropped > 0);

        this.RecordCompactionMeasurements(compactable, compacted, totalBeforeCompaction, estimatedFinalTokens, emergencyMessagesDropped);
        this.RecordPrepareTelemetry(
            activity, outcome, totalBeforeCompaction, estimatedFinalTokens, messagesCompacted, startTimestamp, timeCounting);

        return new PrepareResult(
            final,
            outcome,
            totalBeforeCompaction,
            estimatedFinalTokens,
            messagesCompacted,
            budgetFailureReason,
            emergencyMessagesDropped,
            compacted.SummarizationError);
    }

    /// <summary>
    /// Runs the compaction strategy inside the conversation log scope and the <c>tokenguard.compact</c> activity.
    /// </summary>
    /// <param name="compactable">The unpinned messages handed to the strategy.</param>
    /// <param name="availableTokens">The token budget left after pinned messages.</param>
    /// <param name="cancellationToken">A token that can cancel the strategy.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the strategy result.</returns>
    private async Task<CompactionResult> RunStrategyAsync(
        IReadOnlyList<ContextMessage> compactable, int availableTokens, CancellationToken cancellationToken)
    {
        using var scope = ConversationContextLog.BeginCompactionScope(
            this._logger, this._diagnostics.ConversationId, this._diagnostics.ContextName, this._currentTurn);
        using var activity = TokenGuardTelemetry.ActivitySource.StartActivity(TokenGuardTelemetry.CompactActivityName);

        var timeStrategy = TokenGuardTelemetry.CompactionDuration.Enabled;
        var startTimestamp = timeStrategy ? Stopwatch.GetTimestamp() : 0;

        var compacted = await this._strategy.CompactAsync(compactable, availableTokens, cancellationToken);

        if (timeStrategy)
        {
            TokenGuardTelemetry.CompactionDuration.Record(
                Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds,
                TokenGuardTelemetry.Tag(TokenGuardTelemetry.StrategyTag, compacted.StrategyName), this._diagnostics.ContextNameTag);
        }

        activity?.SetTag(TokenGuardTelemetry.StrategyTag, compacted.StrategyName)
            .SetTag(TokenGuardTelemetry.AvailableTokensTag, availableTokens)
            .SetTag(TokenGuardTelemetry.TokensBeforeTag, compacted.TokensBefore)
            .SetTag(TokenGuardTelemetry.TokensAfterTag, compacted.TokensAfter)
            .SetTag(TokenGuardTelemetry.MessagesAffectedTag, compacted.MessagesAffected);

        return compacted;
    }

    /// <summary>
    /// Completes the <c>tokenguard.prepare</c> activity and records the measurements taken on every prepare call.
    /// </summary>
    /// <param name="activity">The prepare activity, or <see langword="null"/> when no listener is subscribed.</param>
    /// <param name="outcome">The outcome of the prepare call.</param>
    /// <param name="tokensBefore">The estimated token total before compaction.</param>
    /// <param name="tokensAfter">The estimated token total of the prepared payload.</param>
    /// <param name="messagesCompacted">The number of messages compacted or dropped.</param>
    /// <param name="startTimestamp">The timestamp taken when the call started, when the duration is being measured.</param>
    /// <param name="timeCounting">Whether token-counting time was accumulated for this call.</param>
    private void RecordPrepareTelemetry(
        Activity? activity, PrepareOutcome outcome, int tokensBefore, int tokensAfter, int messagesCompacted, long startTimestamp, bool timeCounting)
    {
        var contextTag = this._diagnostics.ContextNameTag;
        var outcomeName = TokenGuardTelemetry.OutcomeName(outcome);
        var outcomeTag = TokenGuardTelemetry.Tag(TokenGuardTelemetry.OutcomeTag, outcomeName);

        if (activity is not null)
        {
            activity.SetTag(TokenGuardTelemetry.TokensBeforeTag, tokensBefore)
                .SetTag(TokenGuardTelemetry.TokensAfterTag, tokensAfter)
                .SetTag(TokenGuardTelemetry.OutcomeTag, outcomeName)
                .SetTag(TokenGuardTelemetry.MessagesCompactedTag, messagesCompacted);

            if (outcome is PrepareOutcome.CompactionInsufficient or PrepareOutcome.CannotCompact)
                activity.SetStatus(ActivityStatusCode.Error, outcomeName);
            else
                activity.SetStatus(ActivityStatusCode.Ok);
        }

        TokenGuardTelemetry.PrepareCount.Add(1, outcomeTag, contextTag);
        TokenGuardTelemetry.ContextTokens.Record(tokensAfter, outcomeTag, contextTag);

        if (TokenGuardTelemetry.PrepareDuration.Enabled)
            TokenGuardTelemetry.PrepareDuration.Record(Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds, outcomeTag, contextTag);

        if (timeCounting)
            TokenGuardTelemetry.TokenCountingDuration.Record(this._counter.StopTiming().TotalSeconds, contextTag);
    }

    /// <summary>
    /// Records the measurements taken only on prepare calls that ran the compaction strategy.
    /// </summary>
    /// <remarks>
    /// The split between masked and summarized messages is read from the <see cref="ContextMessage.State"/> of the
    /// strategy result, and only while the message counter has a listener.
    /// </remarks>
    /// <param name="compactable">The unpinned messages handed to the strategy.</param>
    /// <param name="compacted">The strategy result.</param>
    /// <param name="tokensBefore">The estimated token total before compaction.</param>
    /// <param name="tokensAfter">The estimated token total of the prepared payload.</param>
    /// <param name="emergencyMessagesDropped">The number of messages dropped by emergency truncation.</param>
    private void RecordCompactionMeasurements(
        IReadOnlyList<ContextMessage> compactable, CompactionResult compacted, int tokensBefore, int tokensAfter, int emergencyMessagesDropped)
    {
        var contextTag = this._diagnostics.ContextNameTag;
        TokenGuardTelemetry.TokensReclaimed.Record(tokensBefore - tokensAfter, contextTag);

        if (!TokenGuardTelemetry.CompactionMessages.Enabled)
            return;

        var masked = 0;
        var summaries = 0;
        foreach (var message in compacted.Messages)
        {
            if (message.State == CompactionState.Masked)
                masked++;
            else if (message.State == CompactionState.Summarized)
                summaries++;
        }

        var summarized = summaries > 0 ? compactable.Count - (compacted.Messages.Count - summaries) : 0;

        this.AddCompactionMessages(masked, TokenGuardTelemetry.MaskedKind);
        this.AddCompactionMessages(summarized, TokenGuardTelemetry.SummarizedKind);
        this.AddCompactionMessages(emergencyMessagesDropped, TokenGuardTelemetry.DroppedKind);
    }

    private void AddCompactionMessages(int count, string kind)
    {
        if (count > 0)
        {
            TokenGuardTelemetry.CompactionMessages.Add(
                count, TokenGuardTelemetry.Tag(TokenGuardTelemetry.KindTag, kind), this._diagnostics.ContextNameTag);
        }
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
            ? PrepareOutcome.CannotCompact
            : PrepareOutcome.CompactionInsufficient;
    }

    /// <summary>
    /// Builds the diagnostic message returned for over-budget outcomes.
    /// </summary>
    /// <param name="outcome">The outcome that requires a diagnostic explanation.</param>
    /// <param name="finalTokens">The final token total after preparation.</param>
    /// <param name="messagesCompacted">The number of messages affected during preparation.</param>
    /// <returns>A stable diagnostic string describing why the prepared payload is still over budget.</returns>
    private string BuildBudgetFailureReason(PrepareOutcome outcome, int finalTokens, int messagesCompacted)
    {
        var effectiveMax = (long)this._budget.MaxTokens + this._budget.OverrunToleranceTokens;
        return outcome == PrepareOutcome.CannotCompact
            ? $"Prepared request cannot fit within budget because a single message or preserved content exceeds the limit ({finalTokens} tokens > {effectiveMax} max). Further compaction is impossible."
            : $"Compaction ran but prepared request still exceeds budget ({finalTokens} tokens > {effectiveMax} max). {messagesCompacted} messages were compacted or dropped, but that was insufficient.";
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

        try
        {
            this._health.LogSummary(this._currentTurn);
        }
        catch (Exception)
        {
            // A failing logger must not turn disposal into an error.
        }

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
    /// <see langword="null"/>. Disable emergency truncation by calling
    /// <see cref="Configuration.ConversationConfigBuilder.WithoutEmergencyThreshold"/> on the builder; by default
    /// the library applies a <c>1.0</c> threshold so the emergency pass fires only at the absolute token limit.
    /// </para>
    /// <para>
    /// When enabled, the method identifies eligible drop candidates by excluding pinned messages, which are never
    /// removed. The newest unpinned message is always preserved as an irreducible floor, so the returned list is never
    /// empty, and the latest active turn remains visible to the model.
    /// </para>
    /// <para>
    /// When the prepared list ends with a tool result, the user message that opened that tool loop is kept as well, so
    /// the model still sees the request it is working on. The tool exchanges between that message and the floor are
    /// dropped oldest first like any other candidate.
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
            this.LogEmergencyEvaluation(currentTotal, emergencyLimit, 0, 0, preservedFloorStartIndex, floorExceedsTrigger: true);
            truncated = null;
            return false;
        }

        var loopOpeningUserIndex = FindLoopOpeningUserIndex(prepared, preservedFloorStartIndex);
        var firstKeptIndex = loopOpeningUserIndex ?? preservedFloorStartIndex;

        // Collect drop candidates as atomic units so a tool result is never left without the model
        // message that requested it, which would produce an invalid conversation structure rejected by providers.
        var turnGroups = BuildTurnGroups(prepared, preservedFloorStartIndex, loopOpeningUserIndex);

        if (turnGroups.Count == 0)
        {
            this.LogEmergencyEvaluation(currentTotal, emergencyLimit, 0, 0, firstKeptIndex, floorExceedsTrigger: true);
            truncated = null;
            return false;
        }

        // Drop whole units oldest-first until the budget is satisfied or units are exhausted.
        var dropIndices = new HashSet<int>();
        var total = currentTotal;
        var groupsDropped = 0;
        foreach (var (groupIndices, groupTokens) in turnGroups)
        {
            if (total <= emergencyLimit)
                break;

            foreach (var idx in groupIndices)
                dropIndices.Add(idx);

            total -= groupTokens;
            groupsDropped++;
        }

        this.LogEmergencyEvaluation(
            currentTotal, emergencyLimit, turnGroups.Count, groupsDropped, firstKeptIndex, floorExceedsTrigger: total > emergencyLimit);

        if (dropIndices.Count == 0)
        {
            truncated = null;
            return false;
        }

        truncated = prepared.Where((_, i) => !dropIndices.Contains(i)).ToList();
        return true;
    }

    /// <summary>
    /// Splits the messages before the preserved floor into the units that emergency truncation drops whole.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A turn group starts at each unpinned user message and runs up to the next one. Messages before the first user
    /// message form the first group. A pinned message never opens a group and is never part of a unit.
    /// </para>
    /// <para>
    /// A turn group that ends before the floor is one unit. The floor can start inside a turn group, which is then still
    /// in progress. Its messages before the floor are split further: every message that is not a tool result starts a
    /// unit, so a model message stays with its tool results.
    /// </para>
    /// </remarks>
    /// <param name="prepared">The prepared message list produced for the next provider call.</param>
    /// <param name="limit">The exclusive upper bound for indices that may be considered for dropping.</param>
    /// <param name="keptIndex">
    /// The index of a message before <paramref name="limit"/> that is part of no unit, or <see langword="null"/> when every
    /// unpinned message before <paramref name="limit"/> may be dropped.
    /// </param>
    /// <returns>The units in message order, each paired with its aggregate token count.</returns>
    private static IReadOnlyList<(IReadOnlyList<int> Indices, int Tokens)> BuildTurnGroups(
        IReadOnlyList<ContextMessage> prepared, int limit, int? keptIndex)
    {
        var openGroupStartIndex = FindOpenTurnGroupStartIndex(prepared, limit);
        var groups = new List<(IReadOnlyList<int> Indices, int Tokens)>();
        var i = 0;

        while (i < limit)
        {
            var msg = prepared[i];
            if (msg.IsPinned || i == keptIndex)
            {
                i++;
                continue;
            }

            var messageIndexes = new List<int> { i };
            var messageGroupTokens = msg.TokenCount ?? 0;
            i++;

            while (i < limit && !StartsDropUnit(prepared[i], isInOpenGroup: i >= openGroupStartIndex))
            {
                if (!prepared[i].IsPinned)
                {
                    messageIndexes.Add(i);
                    messageGroupTokens += prepared[i].TokenCount ?? 0;
                }

                i++;
            }

            groups.Add((messageIndexes, messageGroupTokens));
        }

        return groups;
    }

    /// <summary>
    /// Finds where the turn group that the preserved floor starts inside begins.
    /// </summary>
    /// <param name="prepared">The prepared message list produced for the next provider call.</param>
    /// <param name="limit">The index at which the preserved floor starts.</param>
    /// <returns>
    /// The index of the first message of that turn group, or <paramref name="limit"/> when the floor starts on the user
    /// message that opens a turn group.
    /// </returns>
    private static int FindOpenTurnGroupStartIndex(IReadOnlyList<ContextMessage> prepared, int limit)
    {
        if (limit < prepared.Count && OpensTurnGroup(prepared[limit]))
            return limit;

        for (var i = limit - 1; i >= 0; i--)
        {
            if (OpensTurnGroup(prepared[i]))
                return i;
        }

        return 0;
    }

    /// <summary>
    /// Finds the user message that opened the tool loop the prepared list ends in, when it sits before the preserved floor.
    /// </summary>
    /// <remarks>
    /// The model is in a tool loop when the newest unpinned message is a tool result. Emergency truncation keeps the
    /// user message that opened the loop, because without it the model continues with no request in view. A floor that
    /// starts at a summary message already holds every message after the summary.
    /// </remarks>
    /// <param name="prepared">The prepared message list produced for the next provider call.</param>
    /// <param name="floorStartIndex">The index at which the preserved floor starts.</param>
    /// <returns>
    /// The index of the unpinned user message that opens the turn group the floor starts inside, or
    /// <see langword="null"/> when the list does not end with a tool result, the floor starts at a summary message, or
    /// no user message precedes the floor.
    /// </returns>
    private static int? FindLoopOpeningUserIndex(IReadOnlyList<ContextMessage> prepared, int floorStartIndex)
    {
        if (floorStartIndex >= prepared.Count || prepared[floorStartIndex].State == CompactionState.Summarized)
            return null;

        var newestUnpinned = prepared.LastOrDefault(static message => !message.IsPinned);
        if (newestUnpinned?.Role != MessageRole.Tool)
            return null;

        var openGroupStartIndex = FindOpenTurnGroupStartIndex(prepared, floorStartIndex);
        return openGroupStartIndex < floorStartIndex && OpensTurnGroup(prepared[openGroupStartIndex]) ? openGroupStartIndex : null;
    }

    private static bool OpensTurnGroup(ContextMessage message) => message is { Role: MessageRole.User, IsPinned: false };

    private static bool StartsDropUnit(ContextMessage message, bool isInOpenGroup) =>
        OpensTurnGroup(message) || (isInOpenGroup && !message.IsPinned && message.Role != MessageRole.Tool);

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

        var floorStartIndex = this.RepairPreservedFloorStartIndex(prepared, newestUnpinnedIndex);

        // A tool-result tail starts at the model message that produced it. The user message that opened the
        // tool loop is kept separately, so the exchanges between the two stay droppable.
        if (floorStartIndex != newestUnpinnedIndex)
            return floorStartIndex;

        if (prepared[newestUnpinnedIndex].Role != MessageRole.Model)
            return floorStartIndex;

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
    /// Repairs the preserved-floor start so emergency truncation never begins inside a trailing
    /// tool-result tail without the model message that produced it.
    /// </summary>
    /// <param name="prepared">The prepared message list produced for the next provider call.</param>
    /// <param name="newestUnpinnedIndex">The newest unpinned message index in <paramref name="prepared"/>.</param>
    /// <returns>
    /// The repaired floor start index. This equals <paramref name="newestUnpinnedIndex"/> when no
    /// repair is needed.
    /// </returns>
    private int RepairPreservedFloorStartIndex(IReadOnlyList<ContextMessage> prepared, int newestUnpinnedIndex)
    {
        if (prepared[newestUnpinnedIndex].Role != MessageRole.Tool)
            return newestUnpinnedIndex;

        var requiredToolCallIds = new HashSet<string>(
            prepared[newestUnpinnedIndex].Segments
                .OfType<ToolResultContent>()
                .Select(static segment => segment.ToolCallId),
            StringComparer.Ordinal);

        var floorStartIndex = newestUnpinnedIndex;

        for (var i = newestUnpinnedIndex - 1; i >= 0; i--)
        {
            var message = prepared[i];
            if (message.IsPinned || message.Role is not (MessageRole.Model or MessageRole.Tool))
                break;

            floorStartIndex = i;

            foreach (var toolResult in message.Segments.OfType<ToolResultContent>())
            {
                requiredToolCallIds.Add(toolResult.ToolCallId);
            }

            if (message.Role != MessageRole.Model)
                continue;

            var toolCallIds = message.Segments
                .OfType<ToolUseContent>()
                .Select(static segment => segment.ToolCallId);

            if (requiredToolCallIds.IsSubsetOf(toolCallIds))
                return i;
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
    /// Inserts a message into history and updates pinned token bookkeeping.
    /// </summary>
    /// <param name="message">The message to record.</param>
    /// <param name="index">
    /// The optional insertion index. When omitted, the message is appended to the end of history.
    /// </param>
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

        this._historyVersion++;

        var tokenCount = this.EnsureCounted(message);

        if (message.IsPinned)
        {
            this._pinnedTokenTotal += tokenCount;
        }

        this.LogMessageRecorded(message);
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

        this.LogMessageRecorded(message);
    }

    private void LogEmergencyEvaluation(
        int currentTokens, int emergencyTriggerTokens, int turnGroups, int turnGroupsDropped, int preservedFloorIndex, bool floorExceedsTrigger) =>
        ConversationContextLog.EmergencyTruncationEvaluated(
            this._logger, this._diagnostics.ConversationId, this._diagnostics.ContextName, this._currentTurn, currentTokens,
            emergencyTriggerTokens, turnGroups, turnGroupsDropped, preservedFloorIndex, floorExceedsTrigger);

    private void LogMessageRecorded(ContextMessage message) =>
        ConversationContextLog.MessageRecorded(
            this._logger, this._diagnostics.ConversationId, this._diagnostics.ContextName, message.Role, message.IsPinned, message.Segments.Count);

    /// <summary>
    /// Returns a snapshot of the history as the prepared view, with any pinned message recorded inside a tool exchange moved before it.
    /// </summary>
    /// <param name="messages">The recorded history.</param>
    /// <returns>
    /// A new list that holds the messages in recorded order when no pinned message directly precedes a tool result; otherwise
    /// a new list in which each such pinned message precedes the model message that carries the tool calls.
    /// </returns>
    private IReadOnlyList<ContextMessage> MovePinnedMessagesOutOfToolExchanges(IReadOnlyList<ContextMessage> messages)
    {
        if (!HasPinnedMessageBeforeToolResult(messages))
            return messages.ToArray();

        var (pinnedSlots, compactable) = SplitPinned(messages);
        var prepared = ReassemblePreparedMessages(pinnedSlots, compactable, compactable);
        this.LogPinnedPlacements(pinnedSlots, prepared);

        return prepared;
    }

    /// <summary>
    /// Writes one debug record per pinned message with the index it holds in the prepared view.
    /// </summary>
    /// <param name="pinnedSlots">The pinned messages paired with their history indices.</param>
    /// <param name="prepared">The prepared view that contains every pinned message in history order.</param>
    private void LogPinnedPlacements(IReadOnlyList<(int Index, ContextMessage Message)> pinnedSlots, IReadOnlyList<ContextMessage> prepared)
    {
        if (!this._logger.IsEnabled(LogLevel.Debug))
            return;

        var pinnedIndex = 0;
        for (var i = 0; i < prepared.Count && pinnedIndex < pinnedSlots.Count; i++)
        {
            if (!prepared[i].IsPinned)
                continue;

            ConversationContextLog.PinnedMessagePlaced(
                this._logger, this._diagnostics.ConversationId, this._diagnostics.ContextName, this._currentTurn, pinnedSlots[pinnedIndex].Index, i);
            pinnedIndex++;
        }
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
        if (!providerInputTokens.HasValue)
            return;

        this._anchorCorrection = providerInputTokens.Value - this._lastEstimatedTotalTokens;
        this._health.OnProviderTokensReported(providerInputTokens.Value, this._lastEstimatedTotalTokens);

        ConversationContextLog.EstimateAnchored(
            this._logger, this._diagnostics.ConversationId, this._diagnostics.ContextName, providerInputTokens.Value,
            this._lastEstimatedTotalTokens, this._anchorCorrection);

        if (providerInputTokens.Value > 0)
        {
            TokenGuardTelemetry.EstimateErrorRatio.Record(
                (double)this._anchorCorrection / providerInputTokens.Value, this._diagnostics.ContextNameTag);
        }
    }

    /// <summary>
    /// Separates the pinned messages of a history from the messages a strategy may compact.
    /// </summary>
    /// <param name="messages">The recorded history.</param>
    /// <returns>The pinned messages paired with their history indices, and the unpinned messages in history order.</returns>
    private static (List<(int Index, ContextMessage Message)> PinnedSlots, List<ContextMessage> Compactable) SplitPinned(
        IReadOnlyList<ContextMessage> messages)
    {
        var pinnedSlots = new List<(int Index, ContextMessage Message)>();
        var compactable = new List<ContextMessage>(messages.Count);

        for (var i = 0; i < messages.Count; i++)
        {
            if (messages[i].IsPinned)
                pinnedSlots.Add((i, messages[i]));
            else
                compactable.Add(messages[i]);
        }

        return (pinnedSlots, compactable);
    }

    /// <summary>
    /// Checks whether a pinned message was recorded directly before a tool result.
    /// </summary>
    /// <param name="messages">The recorded history.</param>
    /// <returns><see langword="true"/> when at least one pinned message is directly followed by an unpinned tool message.</returns>
    private static bool HasPinnedMessageBeforeToolResult(IReadOnlyList<ContextMessage> messages)
    {
        for (var i = 1; i < messages.Count; i++)
        {
            if (messages[i].Role == MessageRole.Tool && !messages[i].IsPinned && messages[i - 1].IsPinned)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Puts pinned messages back between the unpinned messages they were recorded between.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A pinned message follows every surviving message recorded before it and precedes every surviving message
    /// recorded after it. A summary stands in for the oldest messages, so a pinned message recorded after any of them
    /// follows the summary.
    /// </para>
    /// <para>
    /// The method reads <paramref name="compactedMessages"/> as the newest end of the compactable history: the last
    /// compacted message stands for the last compactable message, the one before it for the one before, and so on,
    /// with the first compacted message standing for everything older.
    /// </para>
    /// <para>
    /// A strategy can keep one older message directly after its summary: the user message that opened the tool loop in
    /// progress. A pinned message recorded after that message follows it.
    /// </para>
    /// </remarks>
    /// <param name="pinnedSlots">The pinned messages paired with their history indices, in history order.</param>
    /// <param name="compactable">The unpinned messages in the history.</param>
    /// <param name="compactedMessages">The unpinned messages after compaction.</param>
    /// <returns>The prepared list with every pinned message in place.</returns>
    private static List<ContextMessage> ReassemblePreparedMessages(
        IReadOnlyList<(int Index, ContextMessage Message)> pinnedSlots,
        IReadOnlyList<ContextMessage> compactable,
        IReadOnlyList<ContextMessage> compactedMessages)
    {
        var prepared = new List<ContextMessage>(pinnedSlots.Count + compactedMessages.Count);
        var replacedCount = Math.Max(0, compactable.Count - compactedMessages.Count);
        var summaryCount = compactedMessages.Count > 0 && compactedMessages[0].State == CompactionState.Summarized ? 1 : 0;
        var keptAfterSummaryIndex = summaryCount == 1 ? FindKeptAfterSummaryIndex(compactable, replacedCount, compactedMessages) : -1;
        var compactedIndex = 0;

        for (var i = 0; i < pinnedSlots.Count; i++)
        {
            // Pinned slots are in history order, so this many unpinned messages were recorded before the pinned one.
            var precedingCount = pinnedSlots[i].Index - i;
            var insertionIndex = precedingCount == 0
                ? 0
                : Math.Clamp(precedingCount - replacedCount, summaryCount, compactedMessages.Count);
            if (keptAfterSummaryIndex >= 0 && insertionIndex == summaryCount && precedingCount > keptAfterSummaryIndex)
                insertionIndex++;

            insertionIndex = MoveBeforeToolExchange(compactedMessages, insertionIndex);

            while (compactedIndex < insertionIndex)
                prepared.Add(compactedMessages[compactedIndex++]);

            prepared.Add(pinnedSlots[i].Message);
        }

        while (compactedIndex < compactedMessages.Count)
            prepared.Add(compactedMessages[compactedIndex++]);

        return prepared;
    }

    /// <summary>
    /// Finds the history position of a user message that a strategy kept directly after its summary.
    /// </summary>
    /// <param name="compactable">The unpinned messages in the history.</param>
    /// <param name="replacedCount">The number of unpinned messages the compacted list is shorter by.</param>
    /// <param name="compactedMessages">The unpinned messages after compaction, starting with a summary message.</param>
    /// <returns>
    /// The index in <paramref name="compactable"/> of the message that follows the summary, when that message is a user
    /// message recorded among the messages the summary replaced; otherwise <c>-1</c>.
    /// </returns>
    private static int FindKeptAfterSummaryIndex(
        IReadOnlyList<ContextMessage> compactable, int replacedCount, IReadOnlyList<ContextMessage> compactedMessages)
    {
        if (compactedMessages.Count < 2 || compactedMessages[1].Role != MessageRole.User)
            return -1;

        for (var i = Math.Min(replacedCount, compactable.Count - 1); i >= 0; i--)
        {
            if (ReferenceEquals(compactable[i], compactedMessages[1]))
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Moves an insertion point that falls inside a tool exchange to just before the model message that opened it.
    /// </summary>
    /// <remarks>
    /// Providers reject a request in which anything separates a model message carrying tool calls from the tool
    /// results that answer them.
    /// </remarks>
    /// <param name="messages">The unpinned messages after compaction.</param>
    /// <param name="insertionIndex">The index of the message a pinned message would be inserted before.</param>
    /// <returns>
    /// The index of the model message that carries the tool calls when <paramref name="insertionIndex"/> points at one
    /// of its tool results; otherwise <paramref name="insertionIndex"/>.
    /// </returns>
    private static int MoveBeforeToolExchange(IReadOnlyList<ContextMessage> messages, int insertionIndex)
    {
        var exchangeStart = insertionIndex;
        while (exchangeStart > 0 && exchangeStart < messages.Count && messages[exchangeStart].Role == MessageRole.Tool)
            exchangeStart--;

        var opensExchange = exchangeStart < insertionIndex && messages[exchangeStart].Segments.Any(static segment => segment is ToolUseContent);
        return opensExchange ? exchangeStart : insertionIndex;
    }
}
