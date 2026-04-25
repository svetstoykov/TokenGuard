namespace TokenGuard.Samples.Console.AgentLoops;

public sealed record ProviderDefinition(
    ProviderKind Kind,
    string Label,
    string ModelId,
    string? Endpoint);

public static class ProviderRegistry
{
    private static readonly ProviderDefinition[] Providers =
    [
        new(ProviderKind.OpenRouter, "OpenRouter", "openai/gpt-5.4-nano", "https://openrouter.ai/api/v1"),
        new(ProviderKind.Anthropic, "Anthropic", "claude-3-haiku-20240307", null),
    ];

    public static IReadOnlyList<ProviderDefinition> All() => Providers;
}
