using Codexplorer.Measurements;

namespace Codexplorer.Automation.Scoring;

/// <summary>Provides shared probe eligibility from observable preparation evidence.</summary>
internal static class ProbeValidity
{
    /// <summary>Checks the last completed prepare before the requested kind counter.</summary>
    /// <param name="requires">The optional validated compaction kind.</param>
    /// <param name="measurements">The non-null session measurements.</param>
    /// <returns>The invalidity reason, or null when eligible.</returns>
    public static string? GetInvalidReason(string? requires, SessionMeasurements measurements)
    {
        var last = measurements.PrepareRecords.Where(record => record.Status == "completed").MaxBy(record => record.Index);
        if (last?.OpeningMessagePresent != false)
            return "instructionNotCompacted";
        var count = requires switch
        {
            "masked" => measurements.MessagesMasked,
            "summarized" => measurements.MessagesSummarized,
            "dropped" => measurements.MessagesDropped,
            _ => 1,
        };
        return count == 0 ? "requiredKindAbsent" : null;
    }
}
