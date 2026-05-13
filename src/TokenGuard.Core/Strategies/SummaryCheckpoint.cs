using TokenGuard.Core.Models;

namespace TokenGuard.Core.Strategies;

/// <summary>
/// Represents a reusable summary for a verified raw conversation prefix.
/// </summary>
/// <param name="SummarizedMessageCount">The number of raw messages represented by the summary.</param>
/// <param name="Fingerprint">The fingerprint of the raw prefix represented by the summary.</param>
/// <param name="SummaryMessage">The synthetic summary message inserted before the raw tail.</param>
internal sealed record SummaryCheckpoint(int SummarizedMessageCount, long Fingerprint, ContextMessage SummaryMessage);
