using TokenGuard.Samples.Console.AgentLoops;

var providerOptions = new (ProviderKind Kind, string Label, string ModelId, string? Endpoint)[]
{
    (ProviderKind.OpenRouter, "OpenRouter", "qwen/qwen3.6-plus", "https://openrouter.ai/api/v1"),
    (ProviderKind.Anthropic, "Anthropic", "claude-3-haiku-20240307", null),
};

Console.WriteLine("=========================================");
Console.WriteLine("   TokenGuard.Core Agentic Loop Sample");
Console.WriteLine("=========================================\n");
Console.WriteLine($"Choose a provider (index 1-{providerOptions.Length}):\n");

for (var i = 0; i < providerOptions.Length; i++)
{
    Console.WriteLine($"{i + 1}. {providerOptions[i].Label} ({providerOptions[i].ModelId})");
}

Console.Write("\nSelection: ");
var selection = Console.ReadLine();

if (!int.TryParse(selection, out var selectedIndex) || selectedIndex < 1 || selectedIndex > providerOptions.Length)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("Invalid selection.");
    Console.ResetColor();
    return;
}

Console.WriteLine();

var selectedProvider = providerOptions[selectedIndex - 1];
var loop = new ProviderSwappableAgentLoop();

await loop.RunAsync(new AgentLoopOptions(
    selectedProvider.Kind,
    selectedProvider.ModelId,
    selectedProvider.Endpoint));
