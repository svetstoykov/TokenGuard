using Codexplorer.Measurements;

namespace Codexplorer.Automation.Scoring;

/// <summary>Provides shared probe eligibility from observable preparation evidence.</summary>
internal static class ProbeValidity
{
    /// <summary>Defines the reason recorded for a probe in a task where nothing was summarized or dropped.</summary>
    public const string NotApplicableReason = "noSummarizationOrDrop";

    /// <summary>Gets eligibility at the first completed prepare after the masked-result question was submitted.</summary>
    /// <param name="measurements">The cumulative session measurements.</param>
    /// <returns>The invalid reason, or null for an eligible masked carrier.</returns>
    public static string? GetMaskedResultInvalidReason(SessionMeasurements measurements)
    {
        var first = measurements.PrepareRecords.Where(record => record.Status == "completed" && record.ProbeQuestionAsked == true)
            .MinBy(record => record.Index);
        return first?.ProbeValueState switch
        {
            null or "unseen" => "factNotRead",
            "removed" => "resultRemoved",
            "visible" => "resultNotMasked",
            "masked" => first.ProbeFactVisibleElsewhere == false ? null : "factVisibleElsewhere",
            _ => "factNotRead"
        };
    }

    /// <summary>Checks whether the task removed any message, which is the only way the opening message can leave the view.</summary>
    /// <param name="measurements">The non-null session measurements.</param>
    /// <returns><see langword="true" /> if at least one message was summarized or dropped; otherwise, <see langword="false" />.</returns>
    public static bool IsApplicable(SessionMeasurements measurements)
    {
        return measurements.MessagesSummarized > 0 || measurements.MessagesDropped > 0;
    }

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
