using Codexplorer.Automation.Reporting;

namespace Codexplorer.Automation.AgentComparison;

/// <summary>Represents independent evidence and final-answer check populations.</summary>
/// <param name="Evidence">The original artifact check outcomes.</param>
/// <param name="FinalAnswer">The original final-answer check outcomes.</param>
internal sealed record ComparisonCheckResults(IReadOnlyList<CheckResult> Evidence, IReadOnlyList<CheckResult> FinalAnswer);
