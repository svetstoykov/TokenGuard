using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Models;
using TokenGuard.Core.Options;

namespace TokenGuard.Core.Strategies;

/// <summary>
/// Applies sliding-window masking first and falls back to LLM summarization only when masking still exceeds budget.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="TieredCompactionStrategy"/> composes <see cref="SlidingWindowStrategy"/> and
/// <see cref="LlmSummarizationStrategy"/> into one policy. This gives callers cheap masking first while keeping a
/// semantic summarization escape hatch for histories that still do not fit.
/// </para>
/// <para>
/// When fallback summarization is required, the strategy intentionally passes the original <see cref="ContextMessage"/>
/// sequence to the summarizer instead of the masked intermediate output. That preserves full tool-result payloads for
/// better summary quality while still using the sliding-window stage as the gate that decides whether the heavier LLM
/// call is necessary.
/// </para>
/// </remarks>
internal sealed class TieredCompactionStrategy : ICompactionStrategy
{
    private readonly SlidingWindowStrategy _slidingWindowStrategy;
    private readonly LlmSummarizationStrategy _llmSummarizationStrategy;

    /// <summary>
    /// Initializes a new instance of the <see cref="TieredCompactionStrategy"/> class.
    /// </summary>
    /// <param name="summarizer">The LLM-backed summarizer used only when sliding-window masking remains over budget.</param>
    /// <param name="options">The forwarded inner-strategy configuration for masking and summarization.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="summarizer"/> is <see langword="null"/>.</exception>
    public TieredCompactionStrategy(ILlmSummarizer summarizer, TieredCompactionOptions options)
    {
        ArgumentNullException.ThrowIfNull(summarizer);

        this._slidingWindowStrategy = new SlidingWindowStrategy(options.SlidingWindowOptions);
        this._llmSummarizationStrategy = new LlmSummarizationStrategy(summarizer, options.LlmSummarizationOptions);
    }

    /// <summary>
    /// Compacts history by attempting masking before escalating to summary replacement.
    /// </summary>
    /// <param name="messages">The ordered compactable message history to process.</param>
    /// <param name="availableTokens">The token budget available to the compacted result after pinned-message costs are removed.</param>
    /// <param name="tokenCounter">The token counter used to measure both masking and summarization outcomes.</param>
    /// <param name="cancellationToken">A token that can cancel the compaction operation.</param>
    /// <returns>
    /// A task that resolves to a <see cref="CompactionResult"/> branded as
    /// <see cref="TieredCompactionStrategy"/> for no-op, masking-only, and summarization outcomes.
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

        var slidingWindowResult = await this._slidingWindowStrategy.CompactAsync(
            messages,
            availableTokens,
            tokenCounter,
            cancellationToken);

        if (slidingWindowResult.TokensAfter <= availableTokens)
        {
            return BuildCompactionResult(slidingWindowResult);
        }

        var summarizationResult = await this._llmSummarizationStrategy.CompactAsync(
            messages,
            availableTokens,
            tokenCounter,
            cancellationToken);

        return BuildCompactionResult(summarizationResult);
    }

    private static CompactionResult BuildCompactionResult(CompactionResult result)
    {
        return new CompactionResult(
            result.Messages,
            result.TokensBefore,
            result.TokensAfter,
            result.MessagesAffected,
            nameof(TieredCompactionStrategy));
    }
}
