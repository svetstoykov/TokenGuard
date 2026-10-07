using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Defaults;
using TokenGuard.Core.Diagnostics;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;
using TokenGuard.Core.Options;

namespace TokenGuard.Core.Strategies;

/// <summary>
/// Replaces older history with one LLM-generated summary while preserving a protected newest-message tail.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="LlmSummarizationStrategy"/> protects exactly <see cref="LlmSummarizationOptions.WindowSize"/> newest
/// compactable messages when that many exist. Messages before the protected tail are passed verbatim to an injected
/// <see cref="ILlmSummarizer"/>, and the returned summary is inserted at the front of the compacted result.
/// </para>
/// <para>
/// While the history ends with a tool result, the newest user message is the request the model is still working on.
/// When that message is older than the protected tail, the compacted result keeps it word for word between the
/// summary and the tail.
/// </para>
/// <para>
/// After one successful summarization pass the strategy stores a lightweight checkpoint for the summarized raw prefix.
/// Later calls validate that checkpoint against the incoming raw prefix, reconstruct a synthetic summary plus raw tail,
/// and reuse or promote that checkpoint without requiring any state from <see cref="TieredCompactionStrategy"/> or
/// <see cref="ConversationContext"/>.
/// </para>
/// <para>
/// Before invoking the summarizer the strategy computes <c>remainingBudget = availableTokens - protectedTailTokens</c>
/// and enforces the configured bounds. For first-time summaries, when <c>remainingBudget</c> is less than
/// <see cref="LlmSummarizationOptions.MinSummaryTokens"/> summarization is skipped and the original messages are
/// returned unchanged. For checkpoint rewrites, the requested target is clamped into the configured
/// <c>[MinSummaryTokens, MaxSummaryTokens]</c> range so repair requests never ask the provider for a one-token summary.
/// </para>
/// <para>
/// When the summarizer throws, or returns a summary that is too large for the budget, the strategy remembers that
/// attempt. A later call with the same messages and the same <c>availableTokens</c> returns the messages unchanged
/// without calling the summarizer. A call with different messages or a different budget makes a new attempt. An
/// attempt that ends because the caller cancelled is not remembered.
/// </para>
/// <para>
/// The strategy sets no time limit on a summarizer call. The call ends when the summarizer returns or throws, which
/// leaves the caller's <see cref="CancellationToken"/> and the provider SDK's network timeout as the only bounds.
/// </para>
/// <para>
/// Checkpoint reuse is intentionally stateful and sequential. One <see cref="LlmSummarizationStrategy"/> instance is
/// expected to serve exactly one conversation flow at a time; concurrent use of the same instance is undefined.
/// </para>
/// </remarks>
internal sealed class LlmSummarizationStrategy : ICompactionStrategy
{
    private readonly ILlmSummarizer _summarizer;
    private readonly ITokenCounter _tokenCounter;
    private readonly LlmSummarizationOptions _options;
    private readonly ILogger _logger;
    private readonly ConversationDiagnostics _diagnostics;
    private SummaryCheckpoint? _checkpoint;

    // The last attempt that threw or overshot. It stops an identical call from paying for the same request again.
    private RejectedSummaryAttempt? _rejectedAttempt;

    // Checkpoint churn is tracked here because only this strategy sees a checkpoint being cleared and rebuilt.
    private bool _checkpointClearedThisRun;
    private int _checkpointRebuildStreak;
    private bool _checkpointChurnActive;

    /// <summary>
    /// Initializes a new instance of the <see cref="LlmSummarizationStrategy"/> class with default options.
    /// </summary>
    /// <param name="summarizer">The summarizer that converts older history into a single text summary.</param>
    /// <param name="tokenCounter">The token counter used to measure the protected tail and final compacted result.</param>
    public LlmSummarizationStrategy(ILlmSummarizer summarizer, ITokenCounter tokenCounter)
        : this(summarizer, tokenCounter, LlmSummarizationOptions.Default)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LlmSummarizationStrategy"/> class.
    /// </summary>
    /// <param name="summarizer">The summarizer that converts older history into a single text summary.</param>
    /// <param name="tokenCounter">The token counter used to measure the protected tail and final compacted result.</param>
    /// <param name="options">The configuration that controls the protected tail size.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="summarizer"/> or <paramref name="tokenCounter"/> is <see langword="null"/>.
    /// </exception>
    public LlmSummarizationStrategy(ILlmSummarizer summarizer, ITokenCounter tokenCounter, LlmSummarizationOptions options)
        : this(summarizer, tokenCounter, options, NullLogger<LlmSummarizationStrategy>.Instance)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LlmSummarizationStrategy"/> class that logs its summarization decisions.
    /// </summary>
    /// <param name="summarizer">The summarizer that converts older history into a single text summary.</param>
    /// <param name="tokenCounter">The token counter used to measure the protected tail and final compacted result.</param>
    /// <param name="options">The configuration that controls the protected tail size.</param>
    /// <param name="logger">The logger that receives the path, skip, and checkpoint records.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="summarizer"/>, <paramref name="tokenCounter"/>, or <paramref name="logger"/> is <see langword="null"/>.
    /// </exception>
    public LlmSummarizationStrategy(
        ILlmSummarizer summarizer, ITokenCounter tokenCounter, LlmSummarizationOptions options, ILogger<LlmSummarizationStrategy> logger)
        : this(
            summarizer, tokenCounter, options, logger,
            new ConversationDiagnostics(NullLoggerFactory.Instance, ConversationDiagnostics.DefaultContextName))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LlmSummarizationStrategy"/> class that reports as part of one conversation.
    /// </summary>
    /// <param name="summarizer">The summarizer that converts older history into a single text summary.</param>
    /// <param name="tokenCounter">The token counter used to measure the protected tail and final compacted result.</param>
    /// <param name="options">The configuration that controls the protected tail size.</param>
    /// <param name="logger">The logger that receives the path, skip, and checkpoint records.</param>
    /// <param name="diagnostics">The identity of the conversation this strategy serves, used to tag its measurements.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="summarizer"/>, <paramref name="tokenCounter"/>, <paramref name="logger"/>, or <paramref name="diagnostics"/>
    /// is <see langword="null"/>.
    /// </exception>
    internal LlmSummarizationStrategy(
        ILlmSummarizer summarizer,
        ITokenCounter tokenCounter,
        LlmSummarizationOptions options,
        ILogger<LlmSummarizationStrategy> logger,
        ConversationDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(summarizer);
        ArgumentNullException.ThrowIfNull(tokenCounter);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(diagnostics);

        this._summarizer = summarizer;
        this._tokenCounter = tokenCounter;
        this._options = options;
        this._logger = logger;
        this._diagnostics = diagnostics;
    }

    /// <summary>
    /// Compacts older history into one summary message while preserving a protected newest-message tail.
    /// </summary>
    /// <param name="messages">The ordered compactable message history to process.</param>
    /// <param name="availableTokens">The number of tokens available to the compacted result after pinned-message costs are removed.</param>
    /// <param name="cancellationToken">A token that can cancel the compaction operation.</param>
    /// <returns>
    /// A task that resolves to a <see cref="CompactionResult"/> containing either the original message sequence when
    /// the history fits fully inside the protected window, or a synthetic summary message followed by the verbatim
    /// protected tail.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="messages"/> is <see langword="null"/>.</exception>
    public async Task<CompactionResult> CompactAsync(
        IReadOnlyList<ContextMessage> messages,
        int availableTokens,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        cancellationToken.ThrowIfCancellationRequested();

        this._checkpointClearedThisRun = false;

        // Measure the original list first so the result can say how much compaction helped.
        var tokensBefore = CountTokens(messages, this._tokenCounter);

        // Pick the newest messages that must stay unchanged.
        var protectedTail = this.GetProtectedTail(messages);

        // Nothing is old enough to summarize, so return the input exactly as it came in.
        if (protectedTail.FirstIndex == 0)
        {
            LlmSummarizationLog.SummarizationSkippedNothingToSummarize(this._logger, messages.Count, this._options.WindowSize);

            return new CompactionResult(
                messages,
                tokensBefore,
                tokensBefore,
                0,
                nameof(LlmSummarizationStrategy));
        }

        // The same history and budget already failed or overshot, so asking again would only repeat the cost.
        if (this.IsRejectedAttempt(messages, availableTokens))
        {
            LlmSummarizationLog.SummarizationSkippedRejectedAttempt(this._logger, messages.Count, availableTokens);
            return CreateUnchangedResult(messages, tokensBefore);
        }

        this._rejectedAttempt = null;

        // Use the saved summary when the old part of the conversation has not changed.
        if (this.TryGetValidCheckpoint(messages, out var checkpoint))
        {
            this.LogPath(LlmSummarizationLog.WithCheckpointPath, messages, tokensBefore, availableTokens, protectedTail);
            this.UpdateCheckpointChurn(rebuiltAfterClear: false);

            return await this.CompactWithCheckpointAsync(
                messages,
                tokensBefore,
                availableTokens,
                protectedTail,
                checkpoint,
                cancellationToken);
        }

        // No saved summary fits this history, so try to create the first one.
        this.LogPath(LlmSummarizationLog.WithoutCheckpointPath, messages, tokensBefore, availableTokens, protectedTail);

        return await this.CompactWithoutCheckpointAsync(messages, tokensBefore, availableTokens, protectedTail, cancellationToken);
    }

    /// <summary>
    /// Writes the debug record that names the summarization path and the numbers it starts from.
    /// </summary>
    /// <param name="path">The name of the path about to run.</param>
    /// <param name="messages">The ordered compactable message history.</param>
    /// <param name="tokensBefore">The token count before compaction.</param>
    /// <param name="availableTokens">The maximum token budget for the compacted result.</param>
    /// <param name="protectedTail">The tail that must remain verbatim.</param>
    private void LogPath(string path, IReadOnlyList<ContextMessage> messages, int tokensBefore, int availableTokens, ProtectedTail protectedTail)
    {
        if (!this._logger.IsEnabled(LogLevel.Debug))
            return;

        var remainingBudget = availableTokens - protectedTail.TokenCount;
        var targetTokens = path == LlmSummarizationLog.WithCheckpointPath
            ? ChooseTargetTokensForCheckpointRewrite(remainingBudget, this._options.MinSummaryTokens, this._options.MaxSummaryTokens)
            : Math.Min(remainingBudget, this._options.MaxSummaryTokens);

        LlmSummarizationLog.SummarizationPathSelected(
            this._logger, path, protectedTail.FirstIndex, messages.Count - protectedTail.FirstIndex, protectedTail.FirstIndex,
            tokensBefore - protectedTail.TokenCount, targetTokens);
    }

    /// <summary>
    /// Finds the newest messages that must stay unchanged and counts their tokens.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The protected tail is the safe part at the end of the conversation. The strategy never rewrites these messages.
    /// Everything before <see cref="ProtectedTail.FirstIndex"/> can be summarized.
    /// </para>
    /// <para>
    /// This method also counts the tail because later code needs to know how much room is left for a summary. The count
    /// includes the user message that opened the tool loop in progress when that message sits before the tail, because
    /// it stays in the result next to the summary.
    /// </para>
    /// </remarks>
    /// <param name="messages">The ordered compactable message history.</param>
    /// <returns>The first message in the tail and the number of tokens used by the messages that stay unchanged.</returns>
    private ProtectedTail GetProtectedTail(IReadOnlyList<ContextMessage> messages)
    {
        var firstProtectedTailIndex = FindFirstProtectedTailIndex(messages, this._options.WindowSize);
        var tokenCount = 0;

        for (var i = firstProtectedTailIndex; i < messages.Count; i++)
        {
            tokenCount += messages[i].TokenCount ?? this._tokenCounter.Count(messages[i]);
        }

        var loopOpeningUserIndex = FindLoopOpeningUserIndex(messages, firstProtectedTailIndex);
        if (loopOpeningUserIndex >= 0)
            tokenCount += messages[loopOpeningUserIndex].TokenCount ?? this._tokenCounter.Count(messages[loopOpeningUserIndex]);

        return new ProtectedTail(firstProtectedTailIndex, tokenCount);
    }

    /// <summary>
    /// Uses a saved summary when the old part of the conversation still matches it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A checkpoint is like a bookmark. It says, "these first messages already became this summary."
    /// </para>
    /// <para>
    /// If that summary plus the newer messages fits, the method reuses it. If more messages became old, it makes a new
    /// bigger summary. If the old summary is too large, it asks the LLM for a smaller summary of the same old messages.
    /// </para>
    /// <para>
    /// In both rewrite paths (promotion and same-boundary refresh), the actual counted result is checked against
    /// <paramref name="availableTokens"/> after the LLM call completes. If the returned summary still overshoots the
    /// budget, the checkpoint is left unchanged and the original messages are returned so emergency truncation can still
    /// act on real history instead of a summary-blocked list.
    /// </para>
    /// </remarks>
    /// <param name="messages">The ordered compactable message history.</param>
    /// <param name="tokensBefore">The token count before compaction.</param>
    /// <param name="availableTokens">The maximum token budget for the compacted result.</param>
    /// <param name="protectedTail">The tail that must remain verbatim.</param>
    /// <param name="checkpoint">The validated checkpoint for the current raw prefix.</param>
    /// <param name="cancellationToken">A token that can cancel the summarization operation.</param>
    /// <returns>The compacted result built from the checkpoint path.</returns>
    private async Task<CompactionResult> CompactWithCheckpointAsync(
        IReadOnlyList<ContextMessage> messages,
        int tokensBefore,
        int availableTokens,
        ProtectedTail protectedTail,
        SummaryCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        // First try the cheapest path: reuse the saved summary and keep the raw tail.
        var cachedResult = CreateSummaryPlusTail(messages, checkpoint.SummarizedMessageCount, checkpoint.SummaryMessage);
        var cachedTokensAfter = CountTokens(cachedResult, this._tokenCounter);

        if (cachedTokensAfter <= availableTokens)
        {
            LlmSummarizationLog.SummaryCheckpointReused(this._logger, checkpoint.SummarizedMessageCount, cachedTokensAfter, availableTokens);

            return new CompactionResult(
                cachedResult,
                tokensBefore,
                cachedTokensAfter,
                messages.Count - cachedResult.Length + 1,
                nameof(LlmSummarizationStrategy));
        }

        // Reuse did not fit, so ask for a smaller or newer summary.
        var targetTokens = ChooseTargetTokensForCheckpointRewrite(
            availableTokens - protectedTail.TokenCount,
            this._options.MinSummaryTokens,
            this._options.MaxSummaryTokens);

        // The tail moved forward. More messages are now old, so include them in the next summary.
        if (protectedTail.FirstIndex > checkpoint.SummarizedMessageCount)
        {
            var promotedFingerprint = ComputeFingerprint(messages, protectedTail.FirstIndex);
            var promotedSummaryMessage = await this.SummarizePrefixAsync(
                messages,
                protectedTail.FirstIndex,
                targetTokens,
                availableTokens,
                cancellationToken);
            var promotedResult = CreateSummaryResult(
                messages,
                tokensBefore,
                protectedTail.FirstIndex,
                promotedSummaryMessage,
                this._tokenCounter);

            if (promotedResult.TokensAfter > availableTokens)
            {
                LlmSummarizationLog.PromotedSummaryOvershot(this._logger, promotedResult.TokensAfter, availableTokens, protectedTail.FirstIndex);
                this.RememberRejectedAttempt(messages, availableTokens);
                return CreateUnchangedResult(messages, tokensBefore);
            }

            this.SetCheckpoint(protectedTail.FirstIndex, promotedFingerprint, promotedSummaryMessage);
            LlmSummarizationLog.SummaryCheckpointCreated(this._logger, protectedTail.FirstIndex, promotedResult.TokensAfter);
            return promotedResult;
        }

        // Same old messages, but the saved summary is too large for this budget.
        var refreshedSummaryMessage = await this.SummarizePrefixAsync(
            messages,
            checkpoint.SummarizedMessageCount,
            targetTokens,
            availableTokens,
            cancellationToken);
        var refreshedResult = CreateSummaryResult(
            messages,
            tokensBefore,
            checkpoint.SummarizedMessageCount,
            refreshedSummaryMessage,
            this._tokenCounter);

        if (refreshedResult.TokensAfter > availableTokens)
        {
            LlmSummarizationLog.RefreshedSummaryOvershot(
                this._logger, refreshedResult.TokensAfter, availableTokens, checkpoint.SummarizedMessageCount);
            this.RememberRejectedAttempt(messages, availableTokens);
            return CreateUnchangedResult(messages, tokensBefore);
        }

        this._checkpoint = checkpoint with { SummaryMessage = refreshedSummaryMessage };
        LlmSummarizationLog.SummaryCheckpointCreated(this._logger, checkpoint.SummarizedMessageCount, refreshedResult.TokensAfter);
        return refreshedResult;
    }

    /// <summary>
    /// Creates the first summary when there is enough room for one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the cold path. The strategy has raw messages only. It must decide whether there is enough room for a new
    /// summary, then save that summary as the first checkpoint. If there is not enough room for a plausible summary
    /// before calling the LLM, it returns the messages unchanged. If the actual returned summary still overshoots
    /// <paramref name="availableTokens"/> after counting, it also returns the messages unchanged and does not commit
    /// the checkpoint, leaving emergency truncation free to act on the original non-summary message set.
    /// </para>
    /// </remarks>
    /// <param name="messages">The ordered compactable message history.</param>
    /// <param name="tokensBefore">The token count before compaction.</param>
    /// <param name="availableTokens">The maximum token budget for the compacted result.</param>
    /// <param name="protectedTail">The tail that must remain verbatim.</param>
    /// <param name="cancellationToken">A token that can cancel the summarization operation.</param>
    /// <returns>The compacted result built without a prior checkpoint.</returns>
    private async Task<CompactionResult> CompactWithoutCheckpointAsync(
        IReadOnlyList<ContextMessage> messages,
        int tokensBefore,
        int availableTokens,
        ProtectedTail protectedTail,
        CancellationToken cancellationToken)
    {
        // The tail must stay unchanged, so only leftover tokens can be used for the summary.
        var remainingBudget = availableTokens - protectedTail.TokenCount;

        // Not enough room for a useful first summary. Return the input unchanged.
        if (remainingBudget < this._options.MinSummaryTokens)
        {
            LlmSummarizationLog.SummarizationSkippedInsufficientBudget(
                this._logger, remainingBudget, this._options.MinSummaryTokens, availableTokens, protectedTail.TokenCount);
            return CreateUnchangedResult(messages, tokensBefore);
        }

        // Ask for a summary that fits the leftover space and the configured maximum.
        var targetTokens = Math.Min(remainingBudget, this._options.MaxSummaryTokens);
        var checkpointFingerprint = ComputeFingerprint(messages, protectedTail.FirstIndex);
        var summaryMessage = await this.SummarizePrefixAsync(
            messages,
            protectedTail.FirstIndex,
            targetTokens,
            availableTokens,
            cancellationToken);

        var summaryResult = CreateSummaryResult(
            messages,
            tokensBefore,
            protectedTail.FirstIndex,
            summaryMessage,
            this._tokenCounter);

        // The LLM may return a summary that is larger than the target. If the actual counted result
        // still overshoots the budget, discard the summary and return the original messages so
        // emergency truncation can still act on real history instead of a summary-blocked list.
        if (summaryResult.TokensAfter > availableTokens)
        {
            LlmSummarizationLog.FirstSummaryOvershot(this._logger, summaryResult.TokensAfter, availableTokens, protectedTail.FirstIndex);
            this.RememberRejectedAttempt(messages, availableTokens);
            return CreateUnchangedResult(messages, tokensBefore);
        }

        this.SetCheckpoint(protectedTail.FirstIndex, checkpointFingerprint, summaryMessage);
        LlmSummarizationLog.SummaryCheckpointCreated(this._logger, protectedTail.FirstIndex, summaryResult.TokensAfter);
        if (this._checkpointClearedThisRun)
            this.UpdateCheckpointChurn(rebuiltAfterClear: true);

        return summaryResult;
    }

    /// <summary>
    /// Finds where the unchanged tail starts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The returned number is an index. Messages at that index and after it stay unchanged. Messages before it can be
    /// summarized.
    /// </para>
    /// <para>
    /// The tail holds at least <paramref name="windowSize"/> messages. It grows when it would start on a tool result, so
    /// the model message that asked for the tool stays with it. It also grows by one message when the message directly
    /// before it is the user message that opened the tool loop in progress.
    /// </para>
    /// </remarks>
    /// <param name="messages">The ordered compactable message history.</param>
    /// <param name="windowSize">The configured newest-message floor to protect.</param>
    /// <returns>The first index kept verbatim in the protected tail.</returns>
    private static int FindFirstProtectedTailIndex(IReadOnlyList<ContextMessage> messages, int windowSize)
    {
        var firstProtectedTailIndex = Math.Max(0, messages.Count - windowSize);
        if (firstProtectedTailIndex == 0)
        {
            return 0;
        }

        firstProtectedTailIndex = MoveBoundaryBeforeToolCallIfNeeded(messages, firstProtectedTailIndex);

        return firstProtectedTailIndex > 0 && FindLoopOpeningUserIndex(messages, firstProtectedTailIndex) == firstProtectedTailIndex - 1
            ? firstProtectedTailIndex - 1
            : firstProtectedTailIndex;
    }

    /// <summary>
    /// Finds the user message that opened the tool loop in progress, when a summary would replace it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The model is in a tool loop when the history ends with a tool result. The newest user message is then the request
    /// the model is still working on, so it stays in the result word for word, directly after the summary.
    /// </para>
    /// </remarks>
    /// <param name="messages">The ordered compactable message history.</param>
    /// <param name="summarizedMessageCount">The number of messages from the start that the summary replaces.</param>
    /// <returns>
    /// The index of the newest user message when the history ends with a tool result and that message is among the
    /// first <paramref name="summarizedMessageCount"/> messages; otherwise <c>-1</c>.
    /// </returns>
    private static int FindLoopOpeningUserIndex(IReadOnlyList<ContextMessage> messages, int summarizedMessageCount)
    {
        if (messages.Count == 0 || messages[^1].Role != MessageRole.Tool)
        {
            return -1;
        }

        for (var i = messages.Count - 1; i >= 0; i--)
        {
            if (messages[i].Role == MessageRole.User)
            {
                return i < summarizedMessageCount ? i : -1;
            }
        }

        return -1;
    }

    /// <summary>
    /// Moves the tail start before a model tool call when the tail starts on a tool result.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Tool calls and tool results belong together. If the tail starts with a tool result, this method moves the tail
    /// start backward so the model message that asked for the tool stays with it.
    /// </para>
    /// <para>
    /// If the method cannot find the model tool call, it returns <c>0</c>. That keeps the full history and avoids a bad
    /// message sequence.
    /// </para>
    /// </remarks>
    /// <param name="messages">The ordered compactable message history.</param>
    /// <param name="firstProtectedTailIndex">The first candidate index kept verbatim in the protected tail.</param>
    /// <returns>The repaired first protected tail index.</returns>
    private static int MoveBoundaryBeforeToolCallIfNeeded(
        IReadOnlyList<ContextMessage> messages,
        int firstProtectedTailIndex)
    {
        if (firstProtectedTailIndex == 0 || messages[firstProtectedTailIndex].Role != MessageRole.Tool)
        {
            return firstProtectedTailIndex;
        }

        while (firstProtectedTailIndex > 0 && messages[firstProtectedTailIndex - 1].Role == MessageRole.Tool)
        {
            firstProtectedTailIndex--;
        }

        if (firstProtectedTailIndex > 0 && messages[firstProtectedTailIndex - 1].Role == MessageRole.Model)
        {
            return firstProtectedTailIndex - 1;
        }

        // Preserve full history when the strategy cannot keep tool-call/tool-result pairing valid.
        return 0;
    }

    /// <summary>
    /// Returns the saved summary only when it still describes the current old messages.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The fingerprint is the quick check. If the current old messages do not match the saved fingerprint, the saved
    /// summary may be about the wrong conversation and must be cleared.
    /// </para>
    /// </remarks>
    /// <param name="messages">The ordered compactable message history.</param>
    /// <param name="checkpoint">The validated checkpoint when one is available.</param>
    /// <returns><see langword="true"/> when the current checkpoint can be used for this history.</returns>
    private bool TryGetValidCheckpoint(IReadOnlyList<ContextMessage> messages, out SummaryCheckpoint checkpoint)
    {
        if (this._checkpoint is null)
        {
            checkpoint = null!;
            return false;
        }

        if (messages.Count < this._checkpoint.SummarizedMessageCount
            || ComputeFingerprint(messages, this._checkpoint.SummarizedMessageCount) != this._checkpoint.Fingerprint)
        {
            LlmSummarizationLog.SummaryCheckpointCleared(
                this._logger,
                messages.Count < this._checkpoint.SummarizedMessageCount
                    ? LlmSummarizationLog.HistoryShorterThanCheckpoint
                    : LlmSummarizationLog.SummarizedPrefixChanged,
                messages.Count, this._checkpoint.SummarizedMessageCount);

            this.ClearCheckpoint();
            this._checkpointClearedThisRun = true;
            checkpoint = null!;
            return false;
        }

        checkpoint = this._checkpoint;
        return true;
    }

    /// <summary>
    /// Counts consecutive runs that cleared and rebuilt the checkpoint and reports the churn signal when it starts or stops.
    /// </summary>
    /// <param name="rebuiltAfterClear">
    /// <see langword="true"/> when this run cleared a checkpoint and saved a new one; <see langword="false"/> when the
    /// saved checkpoint still matched the history.
    /// </param>
    private void UpdateCheckpointChurn(bool rebuiltAfterClear)
    {
        this._checkpointRebuildStreak = rebuiltAfterClear ? this._checkpointRebuildStreak + 1 : 0;

        var churning = this._checkpointRebuildStreak >= ConversationHealthDefaults.CheckpointChurnRuns;
        if (ConversationHealth.Update(
            ref this._checkpointChurnActive, churning, ConversationHealthLog.CheckpointChurn, this._logger, this._diagnostics))
        {
            ConversationHealthLog.CheckpointChurnDetected(
                this._logger, this._diagnostics.ConversationId, this._diagnostics.ContextName, this._checkpointRebuildStreak);
        }
    }

    /// <summary>
    /// Picks the requested summary size when rewriting a saved checkpoint.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Saved summaries may need repair under heavy pressure. This method still keeps rewrite requests inside the
    /// configured <c>[MinSummaryTokens, MaxSummaryTokens]</c> range so the provider never receives an unusably small
    /// target such as <c>1</c>. First-time summaries still use their own skip-path when the remaining budget falls below
    /// <see cref="LlmSummarizationOptions.MinSummaryTokens"/>.
    /// </para>
    /// </remarks>
    /// <param name="remainingBudget">The token budget left after the protected tail.</param>
    /// <param name="minSummaryTokens">The configured minimum summary target.</param>
    /// <param name="maxSummaryTokens">The configured maximum summary target.</param>
    /// <returns>The target token count to request from the summarizer.</returns>
    private static int ChooseTargetTokensForCheckpointRewrite(int remainingBudget, int minSummaryTokens, int maxSummaryTokens)
    {
        return Math.Min(Math.Max(remainingBudget, minSummaryTokens), maxSummaryTokens);
    }

    /// <summary>
    /// Removes the saved summary.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The next compaction cannot reuse old summary state after this runs. It must act like there is no checkpoint.
    /// </para>
    /// </remarks>
    private void ClearCheckpoint()
    {
        this._checkpoint = null;
    }

    /// <summary>
    /// Saves a summary so a later compaction can reuse it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The saved checkpoint stores how many messages the summary covers, the fingerprint for those messages, and the
    /// summary message itself.
    /// </para>
    /// </remarks>
    /// <param name="summarizedMessageCount">The number of raw messages covered by the summary.</param>
    /// <param name="fingerprint">The fingerprint for the covered raw messages.</param>
    /// <param name="summaryMessage">The summary message returned by the summarizer.</param>
    private void SetCheckpoint(int summarizedMessageCount, long fingerprint, ContextMessage summaryMessage)
    {
        this._checkpoint = new SummaryCheckpoint(summarizedMessageCount, fingerprint, summaryMessage);
    }

    /// <summary>
    /// Checks whether the last remembered attempt ran over exactly this history and budget.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The message count and the budget are compared first, so the fingerprint of the whole history is computed only
    /// when both already match.
    /// </para>
    /// </remarks>
    /// <param name="messages">The ordered compactable message history.</param>
    /// <param name="availableTokens">The maximum token budget for the compacted result.</param>
    /// <returns><see langword="true"/> when an identical attempt already threw or overshot the budget.</returns>
    private bool IsRejectedAttempt(IReadOnlyList<ContextMessage> messages, int availableTokens)
    {
        return this._rejectedAttempt is { } rejected
            && rejected.MessageCount == messages.Count
            && rejected.AvailableTokens == availableTokens
            && rejected.Fingerprint == ComputeFingerprint(messages, messages.Count);
    }

    /// <summary>
    /// Remembers that summarizing this history under this budget threw or produced a summary that was too large.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The whole history and the budget identify the attempt, because together they decide which messages are sent to
    /// the summarizer, the requested summary size, and whether the returned summary fits.
    /// </para>
    /// </remarks>
    /// <param name="messages">The ordered compactable message history.</param>
    /// <param name="availableTokens">The maximum token budget for the compacted result.</param>
    private void RememberRejectedAttempt(IReadOnlyList<ContextMessage> messages, int availableTokens)
    {
        this._rejectedAttempt = new RejectedSummaryAttempt(messages.Count, ComputeFingerprint(messages, messages.Count), availableTokens);
    }

    /// <summary>
    /// Builds a result that keeps all messages unchanged.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the fallback when there is no room for a first summary. It leaves the full list alone so the caller can
    /// decide whether emergency truncation should happen.
    /// </para>
    /// </remarks>
    /// <param name="messages">The ordered compactable message history.</param>
    /// <param name="tokensBefore">The token count before compaction.</param>
    /// <returns>A compaction result containing the original message list.</returns>
    private static CompactionResult CreateUnchangedResult(
        IReadOnlyList<ContextMessage> messages,
        int tokensBefore)
    {
        return new CompactionResult(
            messages,
            tokensBefore,
            tokensBefore,
            0,
            nameof(LlmSummarizationStrategy));
    }

    /// <summary>
    /// Builds a result that has one summary followed by the unchanged tail.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the normal successful shape for LLM summarization. Old messages become one summary message. Newer
    /// messages remain exactly as they were.
    /// </para>
    /// </remarks>
    /// <param name="messages">The ordered compactable message history.</param>
    /// <param name="tokensBefore">The token count before compaction.</param>
    /// <param name="summarizedMessageCount">The number of messages replaced by the summary.</param>
    /// <param name="summaryMessage">The summary message placed at the front of the result.</param>
    /// <param name="tokenCounter">The token counter used to count the compacted result.</param>
    /// <returns>A compaction result containing the summary and unchanged tail.</returns>
    private static CompactionResult CreateSummaryResult(
        IReadOnlyList<ContextMessage> messages,
        int tokensBefore,
        int summarizedMessageCount,
        ContextMessage summaryMessage,
        ITokenCounter tokenCounter)
    {
        var compactedMessages = CreateSummaryPlusTail(messages, summarizedMessageCount, summaryMessage);
        var tokensAfter = CountTokens(compactedMessages, tokenCounter);

        return new CompactionResult(
            compactedMessages,
            tokensBefore,
            tokensAfter,
            messages.Count - compactedMessages.Length + 1,
            nameof(LlmSummarizationStrategy));
    }

    /// <summary>
    /// Creates the message list made of one summary plus the unchanged tail.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first item is the summary. Every message after the summarized prefix is copied after it in the same order.
    /// </para>
    /// <para>
    /// When the history ends with a tool result and the summarized prefix holds the user message that opened that tool
    /// loop, that message is copied between the summary and the tail.
    /// </para>
    /// </remarks>
    /// <param name="messages">The ordered compactable message history.</param>
    /// <param name="summarizedMessageCount">The number of messages from the start that the summary covers.</param>
    /// <param name="summaryMessage">The summary message placed at the front.</param>
    /// <returns>The compacted message list.</returns>
    private static ContextMessage[] CreateSummaryPlusTail(
        IReadOnlyList<ContextMessage> messages,
        int summarizedMessageCount,
        ContextMessage summaryMessage)
    {
        var loopOpeningUserIndex = FindLoopOpeningUserIndex(messages, summarizedMessageCount);
        var tailOffset = loopOpeningUserIndex >= 0 ? 2 : 1;
        var result = new ContextMessage[messages.Count - summarizedMessageCount + tailOffset];
        result[0] = summaryMessage;
        if (loopOpeningUserIndex >= 0)
            result[1] = messages[loopOpeningUserIndex];

        for (var i = summarizedMessageCount; i < messages.Count; i++)
        {
            result[(i - summarizedMessageCount) + tailOffset] = messages[i];
        }

        return result;
    }

    /// <summary>
    /// Asks the LLM to summarize the old prefix and wraps the text in a message.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The summarizer returns text only. This method turns that text into a model message and marks it as summarized so
    /// callers can tell it is synthetic.
    /// </para>
    /// <para>
    /// The call has no time limit of its own. When the summarizer throws for a reason other than the caller's
    /// cancellation, the attempt is remembered before the exception is rethrown.
    /// </para>
    /// </remarks>
    /// <param name="messages">The ordered compactable message history.</param>
    /// <param name="summarizedMessageCount">The number of messages to send to the summarizer.</param>
    /// <param name="targetTokens">The requested summary size.</param>
    /// <param name="availableTokens">The maximum token budget for the compacted result.</param>
    /// <param name="cancellationToken">A token that can cancel the summarizer call.</param>
    /// <returns>The synthetic summary message.</returns>
    private async Task<ContextMessage> SummarizePrefixAsync(
        IReadOnlyList<ContextMessage> messages,
        int summarizedMessageCount,
        int targetTokens,
        int availableTokens,
        CancellationToken cancellationToken)
    {
        var messagesToSummarize = messages.Take(summarizedMessageCount).ToArray();

        using var activity = TokenGuardTelemetry.ActivitySource.StartActivity(TokenGuardTelemetry.SummarizeActivityName);
        activity?.SetTag(TokenGuardTelemetry.MessageCountTag, summarizedMessageCount).SetTag(TokenGuardTelemetry.TargetTokensTag, targetTokens);

        var timeCall = TokenGuardTelemetry.SummarizationDuration.Enabled;
        var startTimestamp = timeCall ? Stopwatch.GetTimestamp() : 0;
        this._diagnostics.SummarizerCalls++;

        string summary;
        try
        {
            summary = await this._summarizer.SummarizeAsync(messagesToSummarize, targetTokens, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            activity?.AddException(exception).SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            this._diagnostics.SummarizerFailures++;
            TokenGuardTelemetry.SummarizationFailures.Add(1, this._diagnostics.ContextNameTag);
            this.RecordSummarizationDuration(timeCall, startTimestamp, TokenGuardTelemetry.FailureResult);
            this.RememberRejectedAttempt(messages, availableTokens);
            throw;
        }

        this.RecordSummarizationDuration(timeCall, startTimestamp, TokenGuardTelemetry.SuccessResult);
        return ContextMessage.FromText(MessageRole.Model, summary) with { State = CompactionState.Summarized };
    }

    private void RecordSummarizationDuration(bool timeCall, long startTimestamp, string result)
    {
        if (timeCall)
        {
            TokenGuardTelemetry.SummarizationDuration.Record(
                Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds,
                TokenGuardTelemetry.Tag(TokenGuardTelemetry.ResultTag, result), this._diagnostics.ContextNameTag);
        }
    }

    /// <summary>
    /// Counts the tokens in a message list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Some messages already know their token count. When they do not, this method asks the configured
    /// <see cref="ITokenCounter"/>.
    /// </para>
    /// </remarks>
    /// <param name="messages">The messages to count.</param>
    /// <param name="tokenCounter">The counter used when a message has no cached token count.</param>
    /// <returns>The total token count.</returns>
    private static int CountTokens(IReadOnlyList<ContextMessage> messages, ITokenCounter tokenCounter)
    {
        var count = 0;
        foreach (var message in messages)
        {
            count += message.TokenCount ?? tokenCounter.Count(message);
        }

        return count;
    }

    /// <summary>
    /// Creates a small identity value for the old raw messages.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The fingerprint helps decide whether a checkpoint still matches the current conversation prefix. Each message
    /// contributes its role and the full semantic fingerprint of its segments, including segment type, content, and
    /// tool identity fields where applicable. This ensures that histories that share text content but differ in tool
    /// call identity produce distinct fingerprints and do not incorrectly reuse a stale summary.
    /// </para>
    /// </remarks>
    /// <param name="messages">The ordered compactable message history.</param>
    /// <param name="count">The number of messages from the start to include.</param>
    /// <returns>The fingerprint for the requested prefix.</returns>
    private static long ComputeFingerprint(IReadOnlyList<ContextMessage> messages, int count)
    {
        long fingerprint = 0;

        for (var i = 0; i < count; i++)
        {
            fingerprint = HashCode.Combine(fingerprint, messages[i].Role, GetFingerprintContent(messages[i]));
        }

        return fingerprint;
    }

    /// <summary>
    /// Produces a stable fingerprint string for a single message.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each segment contributes its concrete type name, content, and — for tool segments — its call identifier and
    /// tool name. This prevents histories that share identical text payloads but differ in tool identity from
    /// producing the same fingerprint.
    /// </para>
    /// </remarks>
    /// <param name="message">The message whose content is read.</param>
    /// <returns>The content string used by <see cref="ComputeFingerprint"/>.</returns>
    private static string GetFingerprintContent(ContextMessage message)
    {
        return message.Segments.Count switch
        {
            0 => string.Empty,
            1 => GetSegmentFingerprint(message.Segments[0]),
            _ => string.Join("\n", message.Segments.Select(static segment => GetSegmentFingerprint(segment))),
        };
    }

    /// <summary>
    /// Produces a stable fingerprint string for a single content segment.
    /// </summary>
    /// <param name="segment">The segment to fingerprint.</param>
    /// <returns>A string encoding the segment type, content, and tool identity fields where present.</returns>
    private static string GetSegmentFingerprint(ContentSegment segment)
    {
        return segment switch
        {
            ToolUseContent tool => $"tool_use\n{tool.ToolCallId}\n{tool.ToolName}\n{tool.Content}",
            ToolResultContent result => $"tool_result\n{result.ToolCallId}\n{result.ToolName}\n{result.Content}",
            _ => $"{segment.GetType().Name}\n{segment.Content}",
        };
    }
}
