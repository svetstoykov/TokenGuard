using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Diagnostics;
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
    private readonly LlmSummarizationStrategy? _llmSummarizationStrategy;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TieredCompactionStrategy"/> class.
    /// </summary>
    /// <param name="tokenCounter">The token counter used by the sliding-window stage to evaluate message cost.</param>
    /// <param name="slidingWindowOptions">The masking configuration used for the always-on sliding-window stage.</param>
    /// <param name="llmSummarizationStrategy">
    /// The optional LLM-backed summarization stage used only when masking remains over budget. Pass <see langword="null"/>
    /// to keep tiered compaction in sliding-window-only mode.
    /// </param>
    public TieredCompactionStrategy(
        ITokenCounter tokenCounter,
        SlidingWindowOptions slidingWindowOptions,
        LlmSummarizationStrategy? llmSummarizationStrategy = null)
        : this(
            tokenCounter, slidingWindowOptions, llmSummarizationStrategy, NullLogger<TieredCompactionStrategy>.Instance,
            NullLogger<SlidingWindowStrategy>.Instance)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TieredCompactionStrategy"/> class that logs which stage produced each result.
    /// </summary>
    /// <param name="tokenCounter">The token counter used by the sliding-window stage to evaluate message cost.</param>
    /// <param name="slidingWindowOptions">The masking configuration used for the always-on sliding-window stage.</param>
    /// <param name="llmSummarizationStrategy">
    /// The optional LLM-backed summarization stage used only when masking remains over budget, or <see langword="null"/>
    /// to keep tiered compaction in sliding-window-only mode.
    /// </param>
    /// <param name="logger">The logger that receives one record per compaction call.</param>
    /// <param name="slidingWindowLogger">The logger handed to the sliding-window stage.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="tokenCounter"/>, <paramref name="logger"/>, or <paramref name="slidingWindowLogger"/> is <see langword="null"/>.
    /// </exception>
    public TieredCompactionStrategy(
        ITokenCounter tokenCounter,
        SlidingWindowOptions slidingWindowOptions,
        LlmSummarizationStrategy? llmSummarizationStrategy,
        ILogger<TieredCompactionStrategy> logger,
        ILogger<SlidingWindowStrategy> slidingWindowLogger)
    {
        ArgumentNullException.ThrowIfNull(tokenCounter);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(slidingWindowLogger);

        this._slidingWindowStrategy = new SlidingWindowStrategy(tokenCounter, slidingWindowOptions, slidingWindowLogger);
        this._llmSummarizationStrategy = llmSummarizationStrategy;
        this._logger = logger;
    }

    /// <summary>
    /// Compacts history by attempting masking before escalating to summary replacement.
    /// </summary>
    /// <param name="messages">The ordered compactable message history to process.</param>
    /// <param name="availableTokens">The token budget available to the compacted result after pinned-message costs are removed.</param>
    /// <param name="cancellationToken">A token that can cancel the compaction operation.</param>
    /// <returns>
    /// A task that resolves to a <see cref="CompactionResult"/> branded as
    /// <see cref="TieredCompactionStrategy"/> for no-op, masking-only, and summarization outcomes.
    /// When summarization fires but the actual returned summary still exceeds <paramref name="availableTokens"/>,
    /// the sliding-window result is returned as a summary-free fallback so emergency truncation can act on real history.
    /// When the optional summarization stage fails for a non-cancellation reason, the strategy degrades to the
    /// sliding-window result and reports the captured exception through <see cref="CompactionResult.SummarizationError"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="messages"/> is <see langword="null"/>.</exception>
    public async Task<CompactionResult> CompactAsync(
        IReadOnlyList<ContextMessage> messages,
        int availableTokens,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        cancellationToken.ThrowIfCancellationRequested();

        var slidingWindowResult = await this._slidingWindowStrategy.CompactAsync(
            messages,
            availableTokens,
            cancellationToken);

        if (slidingWindowResult.TokensAfter <= availableTokens || this._llmSummarizationStrategy is null)
        {
            this.LogSelection(
                TieredCompactionLog.SlidingWindowResult,
                slidingWindowResult.TokensAfter <= availableTokens
                    ? TieredCompactionLog.SlidingWindowSufficient
                    : TieredCompactionLog.NoSummarizerConfigured,
                slidingWindowResult, slidingWindowResult, availableTokens);

            return BuildCompactionResult(slidingWindowResult);
        }

        CompactionResult summarizationResult;
        try
        {
            summarizationResult = await this._llmSummarizationStrategy.CompactAsync(
                messages,
                availableTokens,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception summarizationError)
        {
            // The optional summarization stage failed (e.g. provider rate-limit, timeout, network error).
            // Degrade to the masked sliding-window result so the agent loop never crashes, and carry the
            // failure forward so callers can observe it on PrepareResult.SummarizationError.
            this.LogSelection(
                TieredCompactionLog.SlidingWindowResult, TieredCompactionLog.SummarizationThrew, slidingWindowResult, slidingWindowResult,
                availableTokens);

            return BuildCompactionResult(slidingWindowResult, summarizationError);
        }

        // LlmSummarizationStrategy may return original messages when its post-call check finds the
        // actual summary still overshoots. Fall back to the sliding-window result so the caller
        // receives a summary-free list that emergency truncation can still reduce.
        if (summarizationResult.TokensAfter > availableTokens)
        {
            this.LogSelection(
                TieredCompactionLog.SlidingWindowResult, TieredCompactionLog.SummarizationOvershot, slidingWindowResult, slidingWindowResult,
                availableTokens);

            return BuildCompactionResult(slidingWindowResult);
        }

        this.LogSelection(
            TieredCompactionLog.SummarizationResult, TieredCompactionLog.SummarizationSucceeded, slidingWindowResult, summarizationResult,
            availableTokens);

        return BuildCompactionResult(summarizationResult);
    }

    private void LogSelection(string result, string reason, CompactionResult slidingWindowResult, CompactionResult returned, int availableTokens) =>
        TieredCompactionLog.TieredResultSelected(
            this._logger, result, reason, slidingWindowResult.TokensAfter, returned.TokensAfter, availableTokens);

    private static CompactionResult BuildCompactionResult(CompactionResult result, Exception? summarizationError = null)
    {
        return new CompactionResult(
            result.Messages,
            result.TokensBefore,
            result.TokensAfter,
            result.MessagesAffected,
            nameof(TieredCompactionStrategy),
            summarizationError);
    }
}
