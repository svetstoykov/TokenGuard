namespace TokenGuard.Samples.Console.AgentLoops.Providers;

internal sealed record ProviderToolCall(
    string ToolCallId,
    string ToolName,
    string ArgumentsJson);
