namespace TokenGuard.Benchmark.AgentWorkflow.Tasks;

/// <summary>
/// Represents the approximate token volume of a benchmark task for E2E agent-loop runs.
/// </summary>
public enum TaskSize
{
    /// <summary>
    /// Task input is approximately 5–10k tokens.
    /// </summary>
    Small,

    /// <summary>
    /// Task input is approximately 20–30k tokens.
    /// </summary>
    Large,

    /// <summary>
    /// Task input is approximately 50k tokens.
    /// </summary>
    ExtraLarge
}
