using Codexplorer.Automation.Configuration;
using Codexplorer.Automation.Reporting;

namespace Codexplorer.Automation.AgentComparison;

/// <summary>Represents ComparisonReadiness data for the captured comparison.</summary>
/// <param name="SchemaVersion">The readiness schema version.</param>
/// <param name="PlanDigest">The verified complete launch-plan digest.</param>
/// <param name="Ready">Whether every prerequisite passed.</param>
/// <param name="LiveExecutionStarted">Whether inference has started.</param>
/// <param name="CredentialPresent">Whether an existing credential was found.</param>
/// <param name="CredentialSource">The credential source name, without its value.</param>
/// <param name="CheckedAt">The check time.</param>
/// <param name="Blockers">The explicit readiness failures.</param>
internal sealed record ComparisonReadiness(
    int SchemaVersion,
    string PlanDigest,
    bool Ready,
    bool LiveExecutionStarted,
    bool CredentialPresent,
    string CredentialSource,
    DateTimeOffset CheckedAt,
    IReadOnlyList<string> Blockers);
