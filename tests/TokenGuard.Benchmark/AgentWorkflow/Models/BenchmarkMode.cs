namespace TokenGuard.Benchmark.AgentWorkflow.Models;

/// <summary>
/// Identifies how benchmark run manages conversation history.
/// </summary>
public enum BenchmarkMode
{
    /// <summary>
    /// Sends full growing provider message list without TokenGuard context management.
    /// </summary>
    Raw,

    /// <summary>
    /// Sends prepared messages from TokenGuard sliding-window context management.
    /// </summary>
    SlidingWindow,
}
