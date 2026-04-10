using TokenGuard.Samples.Console.AgentLoops;

var loops = new IAgentLoop[]
{
    new CompleteAgentLoop(),
    new MinimalAgentLoop(),
    new AnthropicAgentLoop(),
};

Console.WriteLine("=========================================");
Console.WriteLine("   TokenGuard.Core Agentic Loop Sample");
Console.WriteLine("=========================================\n");
Console.WriteLine($"Choose an agent loop (index 1-{loops.Length}):\n");

for (var i = 0; i < loops.Length; i++)
{
    Console.WriteLine($"{i + 1}. {loops[i].Name}");
}

Console.Write("\nSelection: ");
var selection = Console.ReadLine();

if (!int.TryParse(selection, out var selectedIndex) || selectedIndex < 1 || selectedIndex > loops.Length)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("Invalid selection.");
    Console.ResetColor();
    return;
}

Console.WriteLine();
await loops[selectedIndex - 1].RunAsync();
