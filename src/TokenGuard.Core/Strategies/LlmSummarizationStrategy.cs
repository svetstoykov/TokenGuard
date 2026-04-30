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
/// Before invoking the summarizer the strategy computes <c>remainingBudget = availableTokens - protectedTailTokens</c>
/// and enforces the configured bounds. When <c>remainingBudget</c> is less than
/// <see cref="LlmSummarizationOptions.MinSummaryTokens"/> summarization is skipped and only the protected tail is
/// returned. Otherwise the summarizer receives <c>Math.Min(remainingBudget, MaxSummaryTokens)</c> as its target,
/// ensuring <c>targetTokens</c> is always a positive, bounded value.
/// </para>
/// </remarks>
internal sealed class LlmSummarizationStrategy : ICompactionStrategy
{
    private readonly ILlmSummarizer _summarizer;
    private readonly ITokenCounter _tokenCounter;
    private readonly LlmSummarizationOptions _options;

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
        var messagesToSummarize = messages.Take(boundary).ToArray();
        var summary = await this._summarizer.SummarizeAsync(messagesToSummarize, targetTokens, cancellationToken);

        var result = new ContextMessage[messages.Count - boundary + 1];
        result[0] = ContextMessage.FromText(MessageRole.User, summary) with { State = CompactionState.Summarized };

        for (var i = boundary; i < messages.Count; i++)
        {
            result[(i - boundary) + 1] = messages[i];
        }

        var tokensAfter = CountTokens(result, this._tokenCounter);

        return new CompactionResult(
            result,
            tokensBefore,
            tokensAfter,
            boundary,
            nameof(LlmSummarizationStrategy));
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
}
