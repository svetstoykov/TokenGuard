using TokenGuard.Core.Enums;

namespace TokenGuard.Core.Models;

/// <summary>
/// Carries the prepared message list and metadata describing what happened during context preparation.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="PrepareResult"/> is returned by <see cref="Abstractions.IConversationContext.PrepareAsync"/>.
/// It provides the caller with the messages to send to the provider alongside structured information
/// about whether compaction occurred, how many tokens were affected, and whether the context is in a
/// healthy state for sending.
/// </para>
/// <para>
/// Use <see cref="Outcome"/> to decide whether to proceed with the LLM call.
/// <see cref="PrepareOutcome.Ready"/> and <see cref="PrepareOutcome.Compacted"/> indicate a healthy context.
/// <see cref="PrepareOutcome.CompactionInsufficient"/> means the agent may attempt the call but it will likely be rejected.
/// <see cref="PrepareOutcome.CannotCompact"/> means the call should not be attempted.
/// </para>
/// </remarks>
public sealed record PrepareResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PrepareResult"/> record.
    /// </summary>
    /// <param name="messages">The prepared message list to send to the provider.</param>
    /// <param name="outcome">The outcome describing what happened during preparation.</param>
    /// <param name="tokensBeforeCompaction">The estimated token total before any compaction ran, with the provider correction included.</param>
    /// <param name="tokensAfterCompaction">
    /// The estimated token total of <paramref name="messages"/> after all compaction and truncation, on the same scale as
    /// <paramref name="tokensBeforeCompaction"/>.
    /// </param>
    /// <param name="messagesCompacted">The count of messages removed or replaced during this call.</param>
    /// <param name="budgetFailureReason">A descriptive reason when the outcome still violates the configured budget; null otherwise.</param>
    /// <param name="messagesDropped">
    /// The number of messages dropped by emergency truncation. This excludes messages replaced by the normal compaction
    /// strategy and is zero when emergency truncation did not remove any messages.
    /// </param>
    /// <param name="summarizationError">
    /// The exception captured when an optional LLM summarization stage failed and TokenGuard degraded to sliding-window
    /// masking, or <see langword="null"/> when no summarization failure occurred.
    /// </param>
    public PrepareResult(
        IReadOnlyList<ContextMessage> messages,
        PrepareOutcome outcome,
        int tokensBeforeCompaction,
        int tokensAfterCompaction,
        int messagesCompacted,
        string? budgetFailureReason = null,
        int messagesDropped = 0,
        Exception? summarizationError = null)
    {
        this.Messages = messages ?? throw new ArgumentNullException(nameof(messages));
        this.Outcome = outcome;
        this.TokensBeforeCompaction = tokensBeforeCompaction;
        this.TokensAfterCompaction = tokensAfterCompaction;
        this.MessagesCompacted = messagesCompacted;
        this.BudgetFailureReason = budgetFailureReason;
        this.MessagesDropped = messagesDropped;
        this.SummarizationError = summarizationError;
    }

    /// <summary>
    /// Gets the prepared message list to send to the provider.
    /// </summary>
    public IReadOnlyList<ContextMessage> Messages { get; }

    /// <summary>
    /// Gets the outcome describing what happened during preparation.
    /// </summary>
    public PrepareOutcome Outcome { get; }

    /// <summary>
    /// Gets the estimated token total of the recorded history when <see cref="Abstractions.IConversationContext.PrepareAsync"/>
    /// was called, before any strategy compaction or emergency truncation ran.
    /// </summary>
    /// <remarks>
    /// This total is on the same scale as <see cref="TokensAfterCompaction"/>: the summed message estimates plus the
    /// provider correction, when one is known. The two are equal whenever the call changed no messages.
    /// </remarks>
    public int TokensBeforeCompaction { get; }

    /// <summary>
    /// Gets the estimated token total of <see cref="Messages"/> after all strategy compaction and
    /// emergency truncation completed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The total is the sum of <see cref="ContextMessage.TokenCount"/> over <see cref="Messages"/> plus the provider
    /// correction scaled to those messages. With no provider report recorded, it is that sum alone.
    /// <see cref="Outcome"/> is decided from this total.
    /// </para>
    /// <para>
    /// The provider correction is the input token count last passed to
    /// <see cref="Abstractions.IConversationContext.RecordModelResponse"/> minus the summed message estimates of the payload
    /// that count measured. It stays in effect until the next provider report replaces it.
    /// </para>
    /// <para>
    /// Scaling rule: when the summed estimates of a message list are at least those of the measured payload, the whole
    /// correction is added. When they are smaller, the correction is multiplied by the summed estimates of the list
    /// divided by those of the measured payload, and rounded toward zero. A payload unchanged since the report therefore
    /// totals exactly the provider count, and a payload compacted to half its estimate carries half the correction.
    /// </para>
    /// </remarks>
    public int TokensAfterCompaction { get; }

    /// <summary>
    /// Gets the aggregate count of messages replaced by strategy compaction or dropped by emergency truncation during
    /// this call.
    /// Zero when <see cref="Outcome"/> is <see cref="PrepareOutcome.Ready"/>.
    /// </summary>
    public int MessagesCompacted { get; }

    /// <summary>
    /// Gets a descriptive reason when <see cref="Outcome"/> is <see cref="PrepareOutcome.CompactionInsufficient"/> or
    /// <see cref="PrepareOutcome.CannotCompact"/>; null otherwise.
    /// </summary>
    public string? BudgetFailureReason { get; }

    /// <summary>
    /// Gets the number of messages dropped by emergency truncation.
    /// </summary>
    /// <remarks>
    /// This count includes only messages removed by the emergency stage. It does not include messages replaced or
    /// removed by the configured compaction strategy, which remain part of <see cref="MessagesCompacted"/>. A value of
    /// zero means emergency truncation did not remove any messages during this preparation call.
    /// </remarks>
    public int MessagesDropped { get; }

    /// <summary>
    /// Gets the exception captured when the optional LLM summarization stage failed during this preparation call and
    /// TokenGuard degraded to sliding-window masking; <see langword="null"/> on the happy path.
    /// </summary>
    /// <remarks>
    /// A non-null value does not, by itself, indicate an over-budget result: <see cref="Outcome"/> still reflects the
    /// real budget state of <see cref="Messages"/> after masking and any emergency truncation. Transient summarizer
    /// failures (provider rate-limits, timeouts, network errors) never crash the loop; they surface here so callers can
    /// log them. Caller cancellation is never reported this way — a cancelled <see cref="System.Threading.CancellationToken"/>
    /// still throws <see cref="OperationCanceledException"/> from <see cref="Abstractions.IConversationContext.PrepareAsync"/>.
    /// </remarks>
    public Exception? SummarizationError { get; }
}
