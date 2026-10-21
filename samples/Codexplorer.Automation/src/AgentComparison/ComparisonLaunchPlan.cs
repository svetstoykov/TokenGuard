using Codexplorer.Automation.Configuration;
using Codexplorer.Automation.Reporting;

namespace Codexplorer.Automation.AgentComparison;

/// <summary>Represents ComparisonLaunchPlan data for the captured comparison.</summary>
/// <param name="SchemaVersion">The independent comparison schema version.</param>
/// <param name="CampaignId">The campaign identifier.</param>
/// <param name="RepositoryRoot">The source worktree root.</param>
/// <param name="BaseRef">The user-authorized base ref.</param>
/// <param name="BaseSha">The resolved base commit.</param>
/// <param name="SourceSha">The runtime source commit.</param>
/// <param name="SourceDirtyDigest">The digest of tracked changes and new source files.</param>
/// <param name="PreparedAt">The preparation time.</param>
/// <param name="OutputRoot">The experiment evidence root.</param>
/// <param name="Settings">The enforced upstream configuration.</param>
/// <param name="Bundles">The retained runtime bundles.</param>
/// <param name="Inputs">The source manifests, prompts, catalog and settings evidence.</param>
/// <param name="Sessions">The resolved ordered session matrix.</param>
/// <param name="Blockers">The unresolved preparation blockers.</param>
/// <param name="PlanDigest">The canonical digest excluding this field.</param>
/// <param name="LiveExecutionStarted">Whether public inference has started.</param>
internal sealed record ComparisonLaunchPlan(
    int SchemaVersion,
    string CampaignId,
    string RepositoryRoot,
    string BaseRef,
    string BaseSha,
    string SourceSha,
    string SourceDirtyDigest,
    DateTimeOffset PreparedAt,
    string OutputRoot,
    ComparisonSettings Settings,
    IReadOnlyList<RuntimeBundle> Bundles,
    IReadOnlyList<FileDigest> Inputs,
    IReadOnlyList<ComparisonSessionPlan> Sessions,
    IReadOnlyList<string> Blockers,
    string PlanDigest,
    bool LiveExecutionStarted)
{
    /// <summary>Gets a value indicating whether the runtime source checkout has uncommitted changes.</summary>
    public bool SourceDirty { get; init; }

    /// <summary>Gets a value indicating whether the baseline commit contains uncommitted changes.</summary>
    public bool BaseDirty { get; init; }

    /// <summary>Gets the isolated repository, artifacts and runtime-state templates.</summary>
    public ComparisonIsolationPlan Isolation { get; init; } = ComparisonIsolationPlan.Default;
}
