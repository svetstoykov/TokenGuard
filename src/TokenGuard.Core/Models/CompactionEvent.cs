using TokenGuard.Core.Enums;

namespace TokenGuard.Core.Models;

/// <summary>
/// Represents one compaction cycle observed by a registered compaction observer.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="CompactionEvent"/> is pushed to any observer registered through
/// <see cref="Configuration.ConversationConfigBuilder.WithCompactionObserver(Action{CompactionEvent})"/> from inside
/// <see cref="Abstractions.IConversationContext.PrepareAsync"/>. It carries the same diagnostic fields as
/// <see cref="PrepareResult"/> minus the prepared message payload, so observers can log or emit telemetry without
/// holding a reference to the conversation's message list.
/// </para>
/// <para>
/// This event fires only when <see cref="Outcome"/> is not <see cref="PrepareOutcome.Ready"/> — that is, only when
/// the configured compaction strategy actually ran and changed the outcome classification. Calls that stay within
/// budget without any compaction activity never produce an event.
/// </para>
/// </remarks>
/// <param name="Outcome">The outcome describing what happened during this compaction cycle.</param>
/// <param name="TokensBeforeCompaction">The token total before compaction ran for this <see cref="Abstractions.IConversationContext.PrepareAsync"/> call.</param>
/// <param name="TokensAfterCompaction">The token total after all compaction and emergency truncation completed.</param>
/// <param name="MessagesCompacted">The aggregate count of messages replaced by strategy compaction or dropped by emergency truncation.</param>
/// <param name="MessagesDropped">The number of messages dropped specifically by emergency truncation.</param>
/// <param name="BudgetFailureReason">
/// A descriptive reason when <see cref="Outcome"/> is <see cref="PrepareOutcome.CompactionInsufficient"/> or
/// <see cref="PrepareOutcome.CannotCompact"/>; <see langword="null"/> otherwise.
/// </param>
/// <param name="SummarizationError">
/// The exception captured when an optional LLM summarization stage failed and TokenGuard degraded to sliding-window
/// masking during this cycle, or <see langword="null"/> when no summarization failure occurred.
/// </param>
public sealed record CompactionEvent(
    PrepareOutcome Outcome,
    int TokensBeforeCompaction,
    int TokensAfterCompaction,
    int MessagesCompacted,
    int MessagesDropped,
    string? BudgetFailureReason,
    Exception? SummarizationError);
