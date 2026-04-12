using TokenGuard.E2E.Tasks;

namespace TokenGuard.Benchmark.AgentWorkflow.Tasks;

/// <summary>
/// Provides public access to the built-in E2E agent-loop task definitions.
/// </summary>
/// <remarks>
/// <para>
/// This catalog keeps benchmark and sample projects aligned with the live E2E scenarios without
/// duplicating seeded workspaces, prompts, or completion markers.
/// </para>
/// <para>
/// Each property returns a newly created <see cref="AgentLoopTaskDefinition"/> so callers can run
/// tasks independently without sharing mutable execution state.
/// </para>
/// </remarks>
public static class BuiltInAgentLoopTasks
{
    /// <summary>
    /// Gets the dependency-audit benchmark task definition.
    /// </summary>
    public static AgentLoopTaskDefinition DependencyAudit => DependencyAuditTask.Create();

    /// <summary>
    /// Gets the code-review benchmark task definition.
    /// </summary>
    public static AgentLoopTaskDefinition CodeReview => CodeReviewTask.Create();

    /// <summary>
    /// Gets the release-audit benchmark task definition.
    /// </summary>
    public static AgentLoopTaskDefinition ReleaseAudit => ReleaseAuditTask.Create();

    /// <summary>
    /// Gets the API contract audit benchmark task definition.
    /// </summary>
    public static AgentLoopTaskDefinition ApiContractAudit => ApiContractAuditTask.Create();

    /// <summary>
    /// Gets the database schema evolution audit benchmark task definition.
    /// </summary>
    public static AgentLoopTaskDefinition DatabaseSchemaEvolutionAudit => DatabaseSchemaEvolutionAuditTask.Create();

    /// <summary>
    /// Gets all built-in benchmark task definitions.
    /// </summary>
    /// <returns>
    /// A new list containing the built-in task definitions in stable display order.
    /// </returns>
    public static IReadOnlyList<AgentLoopTaskDefinition> All() =>
    [
        DependencyAuditTask.Create(),
        CodeReviewTask.Create(),
        ReleaseAuditTask.Create(),
        ApiContractAuditTask.Create(),
        DatabaseSchemaEvolutionAuditTask.Create()
    ];
}
