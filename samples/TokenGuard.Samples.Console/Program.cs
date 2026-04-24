using TokenGuard.Benchmark.AgentWorkflow.Tasks;
using TokenGuard.Samples.Console.AgentLoops;

// --- Context budget ---
const int MaxTokens = 30_000;
const double CompactionThreshold = 0.90;
const double EmergencyThreshold = 1.0;

// --- SlidingWindowStrategy ---
const double ProtectedWindowFraction = 0.2;

// --- Agent loop ---
const int MaxIterations = 50;

Console.WriteLine("=========================================");
Console.WriteLine("   TokenGuard.Core Agentic Loop Sample");
Console.WriteLine("=========================================\n");

var task = SelectTask(BuiltInAgentLoopTasks.All());
var provider = SelectProvider(ProviderRegistry.All());

var loop = new AgentLoop(task);

await loop.RunAsync(
    new AgentLoopOptions(provider.Kind, provider.ModelId, provider.Endpoint),
    MaxTokens,
    CompactionThreshold,
    EmergencyThreshold,
    ProtectedWindowFraction,
    MaxIterations);

static AgentLoopTaskDefinition SelectTask(IReadOnlyList<AgentLoopTaskDefinition> tasks)
{
    Console.WriteLine("Select task:");

    for (var i = 0; i < tasks.Count; i++)
    {
        Console.WriteLine($"  {i + 1}. {tasks[i].Name} [{tasks[i].Size}]");
    }

    Console.Write("\nChoice [1]: ");
    var input = Console.ReadLine();
    Console.WriteLine();

    return int.TryParse(input, out var index) && index >= 1 && index <= tasks.Count
        ? tasks[index - 1]
        : tasks[0];
}

static ProviderDefinition SelectProvider(IReadOnlyList<ProviderDefinition> providers)
{
    Console.WriteLine($"Select provider:");

    for (var i = 0; i < providers.Count; i++)
    {
        Console.WriteLine($"  {i + 1}. {providers[i].Label} ({providers[i].ModelId})");
    }

    Console.Write("\nChoice [1]: ");
    var input = Console.ReadLine();
    Console.WriteLine();

    return int.TryParse(input, out var index) && index >= 1 && index <= providers.Count
        ? providers[index - 1]
        : providers[0];
}
