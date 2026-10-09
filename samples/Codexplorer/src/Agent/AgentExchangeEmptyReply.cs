namespace Codexplorer.Agent;

/// <summary>Represents an exchange that stopped because the model returned no text and no tool call twice in a row.</summary>
/// <param name="PartialText">The most recent assistant text, when available.</param>
/// <param name="ModelTurnsCompleted">The completed model turns in the exchange.</param>
internal sealed record AgentExchangeEmptyReply(string? PartialText, int ModelTurnsCompleted) : AgentExchangeResult(ModelTurnsCompleted);
