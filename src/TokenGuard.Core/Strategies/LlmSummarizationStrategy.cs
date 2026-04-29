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
/// The strategy does not attempt to second-guess the summarizer when the protected tail already exhausts
/// <c>availableTokens</c>. It still forwards the original pre-boundary slice together with the computed remaining
/// token budget so the summarizer can choose the best fallback behavior for the active model and prompt.
/// </para>
/// </remarks>
internal sealed class LlmSummarizationStrategy : ICompactionStrategy
{
    private readonly ILlmSummarizer _summarizer;
    private readonly LlmSummarizationOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="LlmSummarizationStrategy"/> class with default options.
    /// </summary>
    /// <param name="summarizer">The summarizer that converts older history into a single text summary.</param>
    public LlmSummarizationStrategy(ILlmSummarizer summarizer)
        : this(summarizer, LlmSummarizationOptions.Default)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LlmSummarizationStrategy"/> class.
    /// </summary>
    /// <param name="summarizer">The summarizer that converts older history into a single text summary.</param>
    /// <param name="options">The configuration that controls the protected tail size.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="summarizer"/> is <see langword="null"/>.</exception>
    public LlmSummarizationStrategy(ILlmSummarizer summarizer, LlmSummarizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(summarizer);

        this._summarizer = summarizer;
        this._options = options;
    }

    /// <summary>
    /// Compacts older history into one summary message while preserving a protected newest-message tail.
    /// </summary>
    /// <param name="messages">The ordered compactable message history to process.</param>
    /// <param name="availableTokens">The number of tokens available to the compacted result after pinned-message costs are removed.</param>
    /// <param name="tokenCounter">The token counter used to measure the protected tail and the final compacted result.</param>
    /// <param name="cancellationToken">A token that can cancel the compaction operation.</param>
    /// <returns>
    /// A task that resolves to a <see cref="CompactionResult"/> containing either the original message sequence when
    /// the history fits fully inside the protected window, or a synthetic summary message followed by the verbatim
    /// protected tail.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="messages"/> or <paramref name="tokenCounter"/> is <see langword="null"/>.
    /// </exception>
    public async Task<CompactionResult> CompactAsync(
        IReadOnlyList<ContextMessage> messages,
        int availableTokens,
        ITokenCounter tokenCounter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(tokenCounter);
        cancellationToken.ThrowIfCancellationRequested();

        var tokensBefore = CountTokens(messages, tokenCounter);
        var boundary = Math.Max(0, messages.Count - this._options.WindowSize);

        if (boundary == 0)
        {
            return new CompactionResult(
                messages,
                tokensBefore,
                tokensBefore,
                0,
                nameof(LlmSummarizationStrategy),
                false);
        }

        var protectedTailTokens = 0;
        for (var i = boundary; i < messages.Count; i++)
        {
            protectedTailTokens += messages[i].TokenCount ?? tokenCounter.Count(messages[i]);
        }

        var targetTokens = availableTokens - protectedTailTokens;
        var messagesToSummarize = messages.Take(boundary).ToArray();
        var summary = await this._summarizer.SummarizeAsync(messagesToSummarize, targetTokens, cancellationToken);

        var result = new ContextMessage[messages.Count - boundary + 1];
        result[0] = ContextMessage.FromText(MessageRole.User, summary) with { State = CompactionState.Summarized };

        for (var i = boundary; i < messages.Count; i++)
        {
            result[(i - boundary) + 1] = messages[i];
        }

        var tokensAfter = CountTokens(result, tokenCounter);

        return new CompactionResult(
            result,
            tokensBefore,
            tokensAfter,
            boundary,
            nameof(LlmSummarizationStrategy),
            true);
    }

    private static int CountTokens(IReadOnlyList<ContextMessage> messages, ITokenCounter tokenCounter)
    {
        var count = 0;
        foreach (var message in messages)
        {
            if (!message.TokenCount.HasValue)
            {
                return tokenCounter.Count(message);
            }
            
            count += message.TokenCount.Value;
        }

        return count;
    }
}
