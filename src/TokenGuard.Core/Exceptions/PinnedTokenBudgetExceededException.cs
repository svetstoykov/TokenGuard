namespace TokenGuard.Core.Exceptions;

/// <summary>
/// Thrown when pinned messages alone exceed the available token budget.
/// </summary>
/// <remarks>
/// <para>
/// This exception signals a configuration error in which non-compactable conversation state already consumes more tokens
/// than the available budget allows. Because pinned messages cannot be masked, summarized, or dropped, TokenGuard fails
/// fast before invoking any compaction strategy.
/// </para>
/// <para>
/// Inspect <see cref="PinnedTokenTotal"/> and <see cref="AvailableTokens"/> to diagnose whether the fixed prompt
/// set, pinned constraints, or other durable instructions need to be reduced.
/// </para>
/// </remarks>
public sealed class PinnedTokenBudgetExceededException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PinnedTokenBudgetExceededException"/> class.
    /// </summary>
    /// <param name="pinnedTokenTotal">The total token cost of all pinned messages.</param>
    /// <param name="availableTokens">The total available token budget after reservations are applied.</param>
    public PinnedTokenBudgetExceededException(int pinnedTokenTotal, int availableTokens)
        : base($"Pinned messages require {pinnedTokenTotal} tokens, which exceeds the available token budget of {availableTokens} tokens.")
    {
        this.PinnedTokenTotal = pinnedTokenTotal;
        this.AvailableTokens = availableTokens;
    }

    /// <summary>
    /// Gets the total token cost of all pinned messages at the time the exception was thrown.
    /// </summary>
    public int PinnedTokenTotal { get; }

    /// <summary>
    /// Gets the total available token budget that the pinned total exceeded.
    /// </summary>
    public int AvailableTokens { get; }
}
