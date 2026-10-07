namespace Codexplorer.Agent;

/// <summary>Represents a terminal stop before preparing another model call.</summary>
/// <param name="PartialText">The most recent assistant text, when available.</param>
/// <param name="ModelTurnsCompleted">The completed model turns in the exchange.</param>
internal sealed record AgentExchangeTurnBudgetReached(string? PartialText, int ModelTurnsCompleted) : AgentExchangeResult(ModelTurnsCompleted);
