using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Options;

namespace TokenGuard.Core.Strategies;

/// <summary>
/// Replaces older history with one LLM-generated summary while preserving a protected newest-message tail.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="LlmSummarizationStrategy"/> protects exactly <see cref="LlmSummarizationOptions.WindowSize"/> newest
/// compactable messages when that many exist. Messages before that boundary are passed verbatim to an injected
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
/// and enforces the configured bounds. When <c>remainingBudget</c> is less than
/// <see cref="LlmSummarizationOptions.MinSummaryTokens"/> summarization is skipped and only the protected tail is
/// returned. Otherwise the summarizer receives <c>Math.Min(remainingBudget, MaxSummaryTokens)</c> as its target,
/// ensuring <c>targetTokens</c> is always a positive, bounded value.
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
    private int _checkpointCoveredCount;
    private long _checkpointPrefixFingerprint;
    private ContextMessage? _checkpointSyntheticSummary;

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

        var tokensBefore = CountTokens(messages, this._tokenCounter);
        var boundary = Math.Max(0, messages.Count - this._options.WindowSize);

        if (this._checkpointCoveredCount > 0 && messages.Count < this._checkpointCoveredCount)
        {
            this.ClearCheckpoint();
        }

        if (boundary == 0)
        {
            return new CompactionResult(
                messages,
                tokensBefore,
                tokensBefore,
                0,
                nameof(LlmSummarizationStrategy));
        }

        var protectedTailTokens = 0;
        for (var i = boundary; i < messages.Count; i++)
        {
            protectedTailTokens += messages[i].TokenCount ?? this._tokenCounter.Count(messages[i]);
        }

        if (this.TryGetValidatedCheckpoint(messages, out var checkpointCoveredCount, out var checkpointSyntheticSummary))
        {
            var cachedResult = BuildCompactedMessages(messages, checkpointCoveredCount, checkpointSyntheticSummary);
            var cachedTokensAfter = CountTokens(cachedResult, this._tokenCounter);

            if (cachedTokensAfter <= availableTokens)
            {
                return new CompactionResult(
                    cachedResult,
                    tokensBefore,
                    cachedTokensAfter,
                    checkpointCoveredCount,
                    nameof(LlmSummarizationStrategy));
            }

            var checkpointTargetTokens = ComputeCheckpointTargetTokens(availableTokens - protectedTailTokens, this._options.MaxSummaryTokens);

            if (boundary > checkpointCoveredCount)
            {
                var promotedFingerprint = ComputeFingerprint(messages, boundary);
                var promotedSummaryMessage = await this.SummarizePrefixAsync(messages, boundary, checkpointTargetTokens, cancellationToken);
                this.SetCheckpoint(boundary, promotedFingerprint, promotedSummaryMessage);
                return CreateCompactionResult(messages, tokensBefore, boundary, promotedSummaryMessage, this._tokenCounter);
            }

            var refreshedSummaryMessage = await this.SummarizePrefixAsync(messages, checkpointCoveredCount, checkpointTargetTokens, cancellationToken);
            this._checkpointSyntheticSummary = refreshedSummaryMessage;
            return CreateCompactionResult(messages, tokensBefore, checkpointCoveredCount, refreshedSummaryMessage, this._tokenCounter);
        }

        var remainingBudget = availableTokens - protectedTailTokens;

        if (remainingBudget < this._options.MinSummaryTokens)
        {
            var protectedTail = messages.Skip(boundary).ToArray();
            var tokensAfterSkip = CountTokens(protectedTail, this._tokenCounter);

            return new CompactionResult(
                protectedTail,
                tokensBefore,
                tokensAfterSkip,
                boundary,
                nameof(LlmSummarizationStrategy));
        }

        var targetTokens = Math.Min(remainingBudget, this._options.MaxSummaryTokens);
        var checkpointFingerprint = ComputeFingerprint(messages, boundary);
        var summaryMessage = await this.SummarizePrefixAsync(messages, boundary, targetTokens, cancellationToken);
        this.SetCheckpoint(boundary, checkpointFingerprint, summaryMessage);

        return CreateCompactionResult(messages, tokensBefore, boundary, summaryMessage, this._tokenCounter);
    }

    private static int CountTokens(IReadOnlyList<ContextMessage> messages, ITokenCounter tokenCounter)
    {
        var count = 0;
        foreach (var message in messages)
        {
            count += message.TokenCount ?? tokenCounter.Count(message);
        }

        return count;
    }

    private static CompactionResult CreateCompactionResult(
        IReadOnlyList<ContextMessage> messages,
        int tokensBefore,
        int summarizedCount,
        ContextMessage summaryMessage,
        ITokenCounter tokenCounter)
    {
        var compactedMessages = BuildCompactedMessages(messages, summarizedCount, summaryMessage);
        var tokensAfter = CountTokens(compactedMessages, tokenCounter);

        return new CompactionResult(
            compactedMessages,
            tokensBefore,
            tokensAfter,
            summarizedCount,
            nameof(LlmSummarizationStrategy));
    }

    private static ContextMessage[] BuildCompactedMessages(
        IReadOnlyList<ContextMessage> messages,
        int summarizedCount,
        ContextMessage summaryMessage)
    {
        var result = new ContextMessage[messages.Count - summarizedCount + 1];
        result[0] = summaryMessage;

        for (var i = summarizedCount; i < messages.Count; i++)
        {
            result[(i - summarizedCount) + 1] = messages[i];
        }

        return result;
    }

    private static long ComputeFingerprint(IReadOnlyList<ContextMessage> messages, int count)
    {
        long fingerprint = 0;

        for (var i = 0; i < count; i++)
        {
            fingerprint = HashCode.Combine((int)fingerprint, messages[i].Role, GetFingerprintContent(messages[i]));
        }

        return fingerprint;
    }

    private static int ComputeCheckpointTargetTokens(int remainingBudget, int maxSummaryTokens)
    {
        return Math.Min(Math.Max(remainingBudget, 1), maxSummaryTokens);
    }

    private static string GetFingerprintContent(ContextMessage message)
    {
        return message.Segments.Count switch
        {
            0 => string.Empty,
            1 => message.Segments[0].Content,
            _ => string.Join("\n", message.Segments.Select(static segment => segment.Content)),
        };
    }

    private void ClearCheckpoint()
    {
        this._checkpointCoveredCount = 0;
        this._checkpointPrefixFingerprint = 0;
        this._checkpointSyntheticSummary = null;
    }

    private void SetCheckpoint(int coveredCount, long prefixFingerprint, ContextMessage summaryMessage)
    {
        this._checkpointCoveredCount = coveredCount;
        this._checkpointPrefixFingerprint = prefixFingerprint;
        this._checkpointSyntheticSummary = summaryMessage;
    }

    private async Task<ContextMessage> SummarizePrefixAsync(
        IReadOnlyList<ContextMessage> messages,
        int boundary,
        int targetTokens,
        CancellationToken cancellationToken)
    {
        var messagesToSummarize = messages.Take(boundary).ToArray();
        var summary = await this._summarizer.SummarizeAsync(messagesToSummarize, targetTokens, cancellationToken);
        return ContextMessage.FromText(MessageRole.User, summary) with { State = CompactionState.Summarized };
    }

    private bool TryGetValidatedCheckpoint(
        IReadOnlyList<ContextMessage> messages,
        out int coveredCount,
        out ContextMessage summaryMessage)
    {
        coveredCount = 0;
        summaryMessage = null!;

        if (this._checkpointCoveredCount == 0
            && this._checkpointPrefixFingerprint == 0
            && this._checkpointSyntheticSummary is null)
        {
            return false;
        }

        if (this._checkpointCoveredCount <= 0 || this._checkpointSyntheticSummary is null)
        {
            this.ClearCheckpoint();
            return false;
        }

        if (messages.Count < this._checkpointCoveredCount
            || ComputeFingerprint(messages, this._checkpointCoveredCount) != this._checkpointPrefixFingerprint)
        {
            this.ClearCheckpoint();
            return false;
        }

        coveredCount = this._checkpointCoveredCount;
        summaryMessage = this._checkpointSyntheticSummary;
        return true;
    }
}
