namespace TokenGuard.Core.Exceptions;

/// <summary>
/// Thrown when pinned messages alone exceed the configured emergency token threshold.
/// </summary>
/// <remarks>
/// <para>
/// This exception signals a configuration error in which non-compactable conversation state already consumes more than
/// the emergency budget allows. Because pinned messages cannot be masked, summarized, or dropped, TokenGuard fails fast
/// before invoking any compaction strategy.
/// </para>
/// <para>
/// Inspect <see cref="PinnedTokenTotal"/> and <see cref="EmergencyTriggerTokens"/> to diagnose whether the fixed prompt
/// set, pinned constraints, or other durable instructions need to be reduced.
/// </para>
/// </remarks>
public sealed class PinnedTokenBudgetExceededException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PinnedTokenBudgetExceededException"/> class.
    /// </summary>
    /// <param name="pinnedTokenTotal">The total token cost of all pinned messages.</param>
    /// <param name="emergencyTriggerTokens">The configured emergency token threshold.</param>
    public PinnedTokenBudgetExceededException(int pinnedTokenTotal, int emergencyTriggerTokens)
        : base($"Pinned messages require {pinnedTokenTotal} tokens, which exceeds the emergency threshold of {emergencyTriggerTokens} tokens.")
    {
        this.PinnedTokenTotal = pinnedTokenTotal;
        this.EmergencyTriggerTokens = emergencyTriggerTokens;
    }

    /// <summary>
    /// Gets the total token cost of all pinned messages at the time the exception was thrown.
    /// </summary>
    public int PinnedTokenTotal { get; }

    /// <summary>
    /// Gets the configured emergency threshold that the pinned total exceeded.
    /// </summary>
    public int EmergencyTriggerTokens { get; }
}
