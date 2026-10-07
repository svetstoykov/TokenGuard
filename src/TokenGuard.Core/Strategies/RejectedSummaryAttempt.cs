namespace TokenGuard.Core.Strategies;

/// <summary>
/// Represents a summarization attempt that failed or produced a summary too large for the budget.
/// </summary>
/// <param name="MessageCount">The number of messages in the history the attempt ran over.</param>
/// <param name="Fingerprint">The fingerprint of every message in that history.</param>
/// <param name="AvailableTokens">The token budget the attempt had to fit.</param>
internal sealed record RejectedSummaryAttempt(int MessageCount, long Fingerprint, int AvailableTokens);
