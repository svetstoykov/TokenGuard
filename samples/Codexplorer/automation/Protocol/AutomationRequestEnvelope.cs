namespace Codexplorer.Automation.Protocol;

internal sealed record AutomationRequestEnvelope(string RequestId, string Command, object? Payload);
