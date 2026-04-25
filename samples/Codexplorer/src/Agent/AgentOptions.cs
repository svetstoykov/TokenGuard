namespace Codexplorer.Agent;

/// <summary>
/// Represents agent-level execution limits for Codexplorer.
/// </summary>
/// <remarks>
/// These options sit above TokenGuard's token budget and bound the overall control loop itself, preventing model or
/// prompt mistakes from spinning indefinitely even when token budget remains available.
/// </remarks>
public sealed record AgentOptions
{
    /// <summary>
    /// Gets maximum number of model turns allowed in one run.
    /// </summary>
    public int MaxTurns { get; init; } = 40;
}
