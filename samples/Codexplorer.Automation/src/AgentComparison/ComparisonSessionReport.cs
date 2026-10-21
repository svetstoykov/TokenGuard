using Codexplorer.Automation.Configuration;
using Codexplorer.Automation.Reporting;

namespace Codexplorer.Automation.AgentComparison;

/// <summary>Represents ComparisonSessionReport data for the captured comparison.</summary>
/// <param name="Session">The resolved session provenance.</param>
/// <param name="StartedAt">The session start.</param>
/// <param name="EndedAt">The session end.</param>
/// <param name="Runtime">The protocol outcome.</param>
/// <param name="EvidenceChecks">The original artifact checks.</param>
/// <param name="FinalAnswerChecks">The original answer checks.</param>
/// <param name="Artifacts">The external artifact inventory.</param>
/// <param name="DiffPath">The captured repository diff path.</param>
/// <param name="RequestTimelinePath">The common recorder timeline path.</param>
internal sealed record ComparisonSessionReport(
    ComparisonSessionPlan Session,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    RuntimeResult Runtime,
    IReadOnlyList<CheckResult> EvidenceChecks,
    IReadOnlyList<CheckResult> FinalAnswerChecks,
    IReadOnlyList<FileDigest> Artifacts,
    string DiffPath,
    string RequestTimelinePath);
