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
    /// Gets the config-migration benchmark task definition.
    /// Designed to run 20–25 turns with small per-turn token output, making it well suited
    /// for demonstrating compaction savings at modest token budgets (5–8K).
    /// </summary>
    public static AgentLoopTaskDefinition ConfigMigration => ConfigMigrationTask.Create();

    /// <summary>
    /// Gets the incident-registry benchmark task definition.
    /// Designed to run 28–30 turns with a growing tool-result payload per turn, which makes
    /// observation masking progressively more valuable as the registry accumulates entries.
    /// </summary>
    public static AgentLoopTaskDefinition IncidentRegistry => IncidentRegistryTask.Create();

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
        DatabaseSchemaEvolutionAuditTask.Create(),
        ConfigMigrationTask.Create(),
        IncidentRegistryTask.Create(),
    ];
}
