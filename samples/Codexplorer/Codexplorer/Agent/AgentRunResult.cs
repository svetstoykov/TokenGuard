namespace Codexplorer.Agent;

/// <summary>
/// Represents the terminal outcome of one explorer-agent run.
/// </summary>
/// <param name="TotalTurns">The number of model turns completed before termination.</param>
public abstract record AgentRunResult(int TotalTurns);

/// <summary>
/// Represents a successful run that produced a final answer.
/// </summary>
/// <param name="FinalText">The final assistant answer text.</param>
/// <param name="TotalTurns">The number of model turns completed before termination.</param>
/// <param name="TotalTokens">The total provider-reported tokens accumulated across model calls.</param>
public sealed record AgentSucceeded(string FinalText, int TotalTurns, int TotalTokens) : AgentRunResult(TotalTurns);

/// <summary>
/// Represents a run that stopped because the prepared context could no longer fit.
/// </summary>
/// <param name="Reason">The human-readable degradation reason.</param>
/// <param name="PartialText">The last partial assistant text seen before degradation, when any.</param>
/// <param name="TotalTurns">The number of model turns completed before termination.</param>
public sealed record AgentDegraded(string Reason, string? PartialText, int TotalTurns) : AgentRunResult(TotalTurns);

/// <summary>
/// Represents a run that hit the configured turn cap before reaching a final answer.
/// </summary>
/// <param name="PartialText">The last partial assistant text seen before the cap was hit, when any.</param>
/// <param name="TotalTurns">The number of model turns completed before termination.</param>
public sealed record AgentMaxTurnsReached(string? PartialText, int TotalTurns) : AgentRunResult(TotalTurns);

/// <summary>
/// Represents a run cancelled by the caller between turns.
/// </summary>
/// <param name="PartialText">The last partial assistant text seen before cancellation, when any.</param>
/// <param name="TotalTurns">The number of model turns completed before termination.</param>
public sealed record AgentCancelled(string? PartialText, int TotalTurns) : AgentRunResult(TotalTurns);

/// <summary>
/// Represents a run that failed because of an unhandled exception.
/// </summary>
/// <param name="Exception">The exception that ended the run.</param>
/// <param name="TotalTurns">The number of model turns completed before termination.</param>
public sealed record AgentFailed(Exception Exception, int TotalTurns) : AgentRunResult(TotalTurns);
