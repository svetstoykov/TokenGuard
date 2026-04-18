using TokenGuard.Benchmark.AgentWorkflow.Tasks;
using TokenGuard.Samples.Console.AgentLoops;

var mode = SelectMode();

if (mode == ExecutionMode.TaskBased)
{
    var tasks = BuiltInAgentLoopTasks.All();
    var selectedTask = SelectTask(tasks);
    await RunTaskBasedLoopAsync(selectedTask.Name);
    return;
}

await RunProviderSwappableLoopAsync();

static ExecutionMode SelectMode()
{
    Console.WriteLine("=========================================");
    Console.WriteLine("   TokenGuard.Core Agentic Loop Sample");
    Console.WriteLine("=========================================\n");
    Console.WriteLine("Select mode:");
    Console.WriteLine("1. Provider-swappable (interactive chat)");
    Console.WriteLine("2. Task-based (run predefined benchmark task)");
    Console.Write("\nChoice [1]: ");
    var input = Console.ReadLine();
    Console.WriteLine();

    return input?.Trim() switch
    {
        "2" => ExecutionMode.TaskBased,
        _ => ExecutionMode.ProviderSwappable,
    };
}

static AgentLoopTaskDefinition SelectTask(IReadOnlyList<AgentLoopTaskDefinition> tasks)
{
    Console.WriteLine("Select task:");

    for (var i = 0; i < tasks.Count; i++)
    {
        Console.WriteLine($"{i + 1}. {tasks[i].Name} [{tasks[i].Size}]");
    }

    Console.Write("\nChoice [1]: ");
    var input = Console.ReadLine();
    Console.WriteLine();

    return int.TryParse(input, out var index) && index >= 1 && index <= tasks.Count
        ? tasks[index - 1]
        : tasks[0];
}

static async Task RunProviderSwappableLoopAsync()
{
    var providerOptions = ProviderRegistry.All();

    Console.WriteLine("=========================================");
    Console.WriteLine("   TokenGuard.Core Agentic Loop Sample");
    Console.WriteLine("=========================================\n");
    Console.WriteLine($"Choose a provider (index 1-{providerOptions.Count}):\n");

    for (var i = 0; i < providerOptions.Count; i++)
    {
        Console.WriteLine($"{i + 1}. {providerOptions[i].Label} ({providerOptions[i].ModelId})");
    }

    Console.Write("\nSelection: ");
    var selection = Console.ReadLine();

    if (!int.TryParse(selection, out var selectedIndex) || selectedIndex < 1 || selectedIndex > providerOptions.Count)
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
}

static async Task RunTaskBasedLoopAsync(string taskName)
{
    var task = FindTask(taskName);
    if (task is null)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Unknown task: {taskName}");
        Console.WriteLine("Available tasks:");
        foreach (var t in BuiltInAgentLoopTasks.All())
        {
            Console.WriteLine($"  - {t.Name}");
        }
        Console.ResetColor();
        return;
    }

    var providerOptions = ProviderRegistry.All();

    Console.WriteLine();
    Console.WriteLine($"Task: {task.Name}");
    Console.WriteLine($"Choose a provider (index 1-{providerOptions.Count}):\n");

    for (var i = 0; i < providerOptions.Count; i++)
    {
        Console.WriteLine($"{i + 1}. {providerOptions[i].Label} ({providerOptions[i].ModelId})");
    }

    Console.Write("\nSelection: ");
    var selection = Console.ReadLine();

    if (!int.TryParse(selection, out var selectedIndex) || selectedIndex < 1 || selectedIndex > providerOptions.Count)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("Invalid selection.");
        Console.ResetColor();
        return;
    }

    Console.WriteLine();

    var selectedProvider = providerOptions[selectedIndex - 1];
    var loop = new TaskBasedAgentLoop(task);

    await loop.RunAsync(new AgentLoopOptions(
        selectedProvider.Kind,
        selectedProvider.ModelId,
        selectedProvider.Endpoint));
}

static AgentLoopTaskDefinition? FindTask(string name)
    => BuiltInAgentLoopTasks.All().FirstOrDefault(t =>
        string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

enum ExecutionMode
{
    ProviderSwappable,
    TaskBased,
}
