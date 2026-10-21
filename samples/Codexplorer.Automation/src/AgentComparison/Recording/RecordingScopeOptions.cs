namespace Codexplorer.Automation.AgentComparison.Recording;

/// <summary>Represents the session identity and call limits for a recording scope.</summary>
/// <param name="SessionId">The identifier attached to every recorded attempt.</param>
/// <param name="MaxAgentCalls">The maximum main-agent requests, including reserved wrap-up calls.</param>
/// <param name="ReservedWrapUpCalls">The number of calls reserved for wrap-up.</param>
/// <param name="Credential">The disposable credential assigned to this child process.</param>
internal sealed record RecordingScopeOptions(string SessionId, int MaxAgentCalls, int ReservedWrapUpCalls, string Credential);
