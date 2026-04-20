using TokenGuard.Core.Models;

namespace TokenGuard.Core.Abstractions;

/// <summary>
/// Receives a notification after each compaction cycle that results in a change to the conversation history.
/// </summary>
/// <remarks>
/// <para>
/// Implement this interface to plug logging, metrics emission, or any other side-effect into the compaction
/// pipeline without modifying <see cref="ConversationContext"/> or the strategy layer.
/// </para>
/// <para>
/// <see cref="OnCompaction"/> is called synchronously on the thread that invoked
/// <c>ConversationContext.PrepareAsync</c>, immediately after the compaction strategy returns and only when
/// <see cref="CompactionResult.WasApplied"/> is <see langword="true"/>. Implementations should be fast.
/// If async work is required, queue it internally rather than blocking the caller.
/// </para>
/// </remarks>
public interface ICompactionObserver
{
    /// <summary>
    /// Called once per compaction cycle in which the history was changed.
    /// </summary>
    /// <param name="compactionEvent">
    /// A snapshot of the compaction outcome, including the compaction result, the time the cycle ran,
    /// the threshold that triggered it, and the budget that was in effect at trigger time.
    /// </param>
    void OnCompaction(CompactionEvent compactionEvent);
}
