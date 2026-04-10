namespace TokenGuard.Samples.Console.AgentLoops;

public sealed record AgentLoopOptions(
    ProviderKind Provider,
    string ModelId,
    string? Endpoint,
    bool VerboseLogging = true);
