using Codexplorer.Automation.AgentComparison.Recording;

namespace Codexplorer.Automation.AgentComparison;

/// <summary>Represents one comparison invocation with independently captured provider accounting.</summary>
/// <param name="SchemaVersion">The comparison schema version.</param>
/// <param name="Synthetic">Whether all model responses came from a local fake.</param>
/// <param name="LiveExecutionStarted">Whether public inference started.</param>
/// <param name="PlanDigest">The immutable launch-plan binding, or synthetic marker.</param>
/// <param name="Session">The runtime outcome and original check populations.</param>
/// <param name="Usage">The nullable provider totals.</param>
/// <param name="Attempts">The raw unique attempt records.</param>
/// <param name="Limitations">The explicit measurement limits.</param>
internal sealed record ComparisonExecutionReport(int SchemaVersion, bool Synthetic, bool LiveExecutionStarted, string PlanDigest,
    ComparisonSessionReport Session, ComparisonUsageSummary Usage, IReadOnlyList<RecordedAttempt> Attempts, IReadOnlyList<string> Limitations);
