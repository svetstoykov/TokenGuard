using TokenGuard.Core.Abstractions;
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
/// Checkpoint reuse is intentionally stateful and sequential. One <see cref="LlmSummarizationStrategy"/> instance is
/// expected to serve exactly one conversation flow at a time; concurrent use of the same instance is undefined.
/// </para>
/// </remarks>
internal sealed class LlmSummarizationStrategy : ICompactionStrategy
{
    private readonly ILlmSummarizer _summarizer;
    private readonly ITokenCounter _tokenCounter;
    private readonly LlmSummarizationOptions _options;
    private SummaryCheckpoint? _checkpoint;

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
    {
        ArgumentNullException.ThrowIfNull(summarizer);
        ArgumentNullException.ThrowIfNull(tokenCounter);

        this._summarizer = summarizer;
        this._tokenCounter = tokenCounter;
        this._options = options;
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

        // Measure the original list first so the result can say how much compaction helped.
        var tokensBefore = CountTokens(messages, this._tokenCounter);

        // Pick the newest messages that must stay unchanged.
        var protectedTail = this.GetProtectedTail(messages);

        // Nothing is old enough to summarize, so return the input exactly as it came in.
        if (protectedTail.FirstIndex == 0)
        {
            return new CompactionResult(
                messages,
                tokensBefore,
                tokensBefore,
                0,
                nameof(LlmSummarizationStrategy));
        }

        // Use the saved summary when the old part of the conversation has not changed.
        if (this.TryGetValidCheckpoint(messages, out var checkpoint))
        {
            return await this.CompactWithCheckpointAsync(
                messages,
                tokensBefore,
                availableTokens,
                protectedTail,
                checkpoint,
                cancellationToken);
        }

        // No saved summary fits this history, so try to create the first one.
        return await this.CompactWithoutCheckpointAsync(messages, tokensBefore, availableTokens, protectedTail, cancellationToken);
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
    /// This method also counts the tail because later code needs to know how much room is left for a summary.
    /// </para>
    /// </remarks>
    /// <param name="messages">The ordered compactable message history.</param>
    /// <returns>The first message in the tail and the number of tokens used by the tail.</returns>
    private ProtectedTail GetProtectedTail(IReadOnlyList<ContextMessage> messages)
    {
        var firstProtectedTailIndex = FindFirstProtectedTailIndex(messages, this._options.WindowSize);
        var tokenCount = 0;

        for (var i = firstProtectedTailIndex; i < messages.Count; i++)
        {
            tokenCount += messages[i].TokenCount ?? this._tokenCounter.Count(messages[i]);
        }

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
            return new CompactionResult(
                cachedResult,
                tokensBefore,
                cachedTokensAfter,
                checkpoint.SummarizedMessageCount,
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
                cancellationToken);
            this.SetCheckpoint(protectedTail.FirstIndex, promotedFingerprint, promotedSummaryMessage);
            return CreateSummaryResult(
                messages,
                tokensBefore,
                protectedTail.FirstIndex,
                promotedSummaryMessage,
                this._tokenCounter);
        }

        // Same old messages, but the saved summary is too large for this budget.
        var refreshedSummaryMessage = await this.SummarizePrefixAsync(
            messages,
            checkpoint.SummarizedMessageCount,
            targetTokens,
            cancellationToken);
        this._checkpoint = checkpoint with { SummaryMessage = refreshedSummaryMessage };
        return CreateSummaryResult(
            messages,
            tokensBefore,
            checkpoint.SummarizedMessageCount,
            refreshedSummaryMessage,
            this._tokenCounter);
    }

    /// <summary>
    /// Creates the first summary when there is enough room for one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the cold path. The strategy has raw messages only. It must decide whether there is enough room for a new
    /// summary, then save that summary as the first checkpoint. If there is not enough room, it returns the messages
    /// unchanged because emergency truncation belongs to <see cref="ConversationContext"/>.
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
            return CreateUnchangedResult(messages, tokensBefore);
        }

        // Ask for a summary that fits the leftover space and the configured maximum.
        var targetTokens = Math.Min(remainingBudget, this._options.MaxSummaryTokens);
        var checkpointFingerprint = ComputeFingerprint(messages, protectedTail.FirstIndex);
        var summaryMessage = await this.SummarizePrefixAsync(
            messages,
            protectedTail.FirstIndex,
            targetTokens,
            cancellationToken);
        this.SetCheckpoint(protectedTail.FirstIndex, checkpointFingerprint, summaryMessage);

        return CreateSummaryResult(
            messages,
            tokensBefore,
            protectedTail.FirstIndex,
            summaryMessage,
            this._tokenCounter);
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
    /// <see cref="ConversationContext"/> marks turns, so the method can keep whole turns together. Manually created
    /// messages often do not have useful turn values, so the method falls back to tool-call repair.
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

        if (HasRecordedTurnBoundaries(messages))
        {
            var turn = messages[firstProtectedTailIndex].Turn;

            // WindowSize is a floor; keep whole turns when ConversationContext recorded turn markers.
            while (firstProtectedTailIndex > 0 && messages[firstProtectedTailIndex - 1].Turn == turn)
            {
                firstProtectedTailIndex--;
            }

            return firstProtectedTailIndex;
        }

        // Manual messages may not have reliable Turn markers, so repair only tool-call pairing.
        return MoveBoundaryBeforeToolCallIfNeeded(messages, firstProtectedTailIndex);
    }

    /// <summary>
    /// Checks whether messages have real turn numbers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When every message has the same turn, the strategy treats turn data as missing. This happens often in tests and
    /// direct strategy calls.
    /// </para>
    /// </remarks>
    /// <param name="messages">The ordered compactable message history.</param>
    /// <returns><see langword="true"/> when at least one adjacent message crosses a recorded turn boundary.</returns>
    private static bool HasRecordedTurnBoundaries(IReadOnlyList<ContextMessage> messages)
    {
        for (var i = 1; i < messages.Count; i++)
        {
            if (messages[i - 1].Turn != messages[i].Turn)
            {
                return true;
            }
        }

        return false;
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
            this.ClearCheckpoint();
            checkpoint = null!;
            return false;
        }

        checkpoint = this._checkpoint;
        return true;
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
            summarizedMessageCount,
            nameof(LlmSummarizationStrategy));
    }

    /// <summary>
    /// Creates the message list made of one summary plus the unchanged tail.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first item is the summary. Every message after the summarized prefix is copied after it in the same order.
    /// </para>
    /// </remarks>
    /// <param name="messages">The ordered compactable message history.</param>
    /// <param name="summarizedMessageCount">The number of messages replaced by the summary.</param>
    /// <param name="summaryMessage">The summary message placed at the front.</param>
    /// <returns>The compacted message list.</returns>
    private static ContextMessage[] CreateSummaryPlusTail(
        IReadOnlyList<ContextMessage> messages,
        int summarizedMessageCount,
        ContextMessage summaryMessage)
    {
        var result = new ContextMessage[messages.Count - summarizedMessageCount + 1];
        result[0] = summaryMessage;

        for (var i = summarizedMessageCount; i < messages.Count; i++)
        {
            result[(i - summarizedMessageCount) + 1] = messages[i];
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
    /// </remarks>
    /// <param name="messages">The ordered compactable message history.</param>
    /// <param name="summarizedMessageCount">The number of messages to send to the summarizer.</param>
    /// <param name="targetTokens">The requested summary size.</param>
    /// <param name="cancellationToken">A token that can cancel the summarizer call.</param>
    /// <returns>The synthetic summary message.</returns>
    private async Task<ContextMessage> SummarizePrefixAsync(
        IReadOnlyList<ContextMessage> messages,
        int summarizedMessageCount,
        int targetTokens,
        CancellationToken cancellationToken)
    {
        var messagesToSummarize = messages.Take(summarizedMessageCount).ToArray();
        var summary = await this._summarizer.SummarizeAsync(messagesToSummarize, targetTokens, cancellationToken);
        return ContextMessage.FromText(MessageRole.Model, summary) with { State = CompactionState.Summarized };
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
