namespace TokenGuard.Core.Models;

/// <summary>
/// Represents the outcome and diagnostics for one compaction cycle.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CompactionResult"/> carries both the transformed message sequence and the metrics needed to understand
/// what changed during a single compaction cycle. It allows callers such as <see cref="ConversationContext"/> to keep
/// using the compacted messages while also preserving observability for diagnostics and future notification pipelines.
/// </para>
/// <para>
/// Implementations should populate <see cref="TokensBefore"/>, <see cref="TokensAfter"/>,
/// <see cref="MessagesAffected"/> and <see cref="StrategyName"/> so downstream consumers can inspect the aggregate
/// cycle outcome without depending on strategy-specific compaction classifications.
/// </para>
/// </remarks>
public sealed record CompactionResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CompactionResult"/> record.
    /// </summary>
    /// <param name="messages">The ordered messages produced by the compaction cycle.</param>
    /// <param name="tokensBefore">The aggregate token count before the compaction cycle ran.</param>
    /// <param name="tokensAfter">
    /// The aggregate token count across <paramref name="messages"/> after all strategy compaction and emergency
    /// truncation represented by this result completed.
    /// </param>
    /// <param name="messagesAffected">
    /// The aggregate number of messages replaced by strategy compaction or dropped by emergency truncation.
    /// </param>
    /// <param name="strategyName">The strategy identifier reported by the compaction implementation.</param>
    /// <param name="summarizationError">
    /// The exception captured when an optional LLM summarization stage failed and the compaction degraded to a
    /// non-summary result, or <see langword="null"/> when no summarization failure occurred.
    /// </param>
    public CompactionResult(
        IReadOnlyList<ContextMessage> messages,
        int tokensBefore,
        int tokensAfter,
        int messagesAffected,
        string strategyName,
        Exception? summarizationError = null)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyName);

        this.Messages = messages;
        this.TokensBefore = tokensBefore;
        this.TokensAfter = tokensAfter;
        this.MessagesAffected = messagesAffected;
        this.StrategyName = strategyName;
        this.SummarizationError = summarizationError;
    }

    /// <summary>
    /// Gets the ordered messages produced by the compaction cycle.
    /// </summary>
    public IReadOnlyList<ContextMessage> Messages { get; }

    /// <summary>
    /// Gets the aggregate token count before the compaction cycle ran.
    /// </summary>
    public int TokensBefore { get; }

    /// <summary>
    /// Gets the aggregate token count across <see cref="Messages"/> after all strategy compaction and emergency
    /// truncation represented by this result completed.
    /// </summary>
    public int TokensAfter { get; }

    /// <summary>
    /// Gets the aggregate number of messages replaced by strategy compaction or dropped by emergency truncation.
    /// </summary>
    public int MessagesAffected { get; }

    /// <summary>
    /// Gets the strategy identifier reported by the compaction implementation.
    /// </summary>
    public string StrategyName { get; }

    /// <summary>
    /// Gets the exception captured when an optional LLM summarization stage failed and compaction degraded to a
    /// non-summary result; <see langword="null"/> when no summarization failure occurred.
    /// </summary>
    /// <remarks>
    /// A non-null value means TokenGuard caught a failure from the summarizer (for example a provider rate-limit,
    /// timeout, or network error) and fell back to sliding-window masking. The returned <see cref="Messages"/> are
    /// still safe to send; this value exists so callers can log why semantic summarization did not run.
    /// </remarks>
    public Exception? SummarizationError { get; }
}
