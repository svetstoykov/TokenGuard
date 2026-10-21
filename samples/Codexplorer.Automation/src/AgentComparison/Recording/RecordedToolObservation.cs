namespace Codexplorer.Automation.AgentComparison.Recording;

/// <summary>Represents a tool call observed in the submitted request history.</summary>
/// <param name="CallId">The native call identifier.</param>
/// <param name="Name">The native tool name.</param>
/// <param name="ArgumentBytes">The measured UTF-8 size of the argument string.</param>
/// <param name="ResultBytes">The measured UTF-8 result-content size when present.</param>
internal sealed record RecordedToolObservation(string CallId, string Name, int ArgumentBytes, int? ResultBytes);
