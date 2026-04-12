using TokenGuard.Benchmark.AgentWorkflow;
using TokenGuard.Benchmark.AgentWorkflow.Models;
using TokenGuard.Benchmark.AgentWorkflow.Tasks;
using TokenGuard.Benchmark.Reporting;

Console.WriteLine("TokenGuard Agent Workflow Benchmark");
Console.WriteLine();

var tasks = BuiltInAgentLoopTasks.All();
var selectedTasks = SelectRunAll()
    ? tasks
    : [SelectTask(tasks)];
var selectedModes = SelectBenchmarkModes();

var maxIterations = ReadInt("Max iterations", BenchmarkConfiguration.Raw.MaxIterations, minimum: 1);
var maxTokens = selectedModes.Contains(BenchmarkMode.SlidingWindow)
    ? ReadInt(
        "Managed max tokens",
        BenchmarkConfiguration.SlidingWindow.MaxTokens ?? 80_000,
        minimum: 1_000)
    : BenchmarkConfiguration.SlidingWindow.MaxTokens ?? 80_000;
var compactionThreshold = selectedModes.Contains(BenchmarkMode.SlidingWindow)
    ? ReadDouble(
        "Managed compaction threshold",
        BenchmarkConfiguration.SlidingWindow.CompactionThreshold ?? 0.80,
        minimum: 0.05,
        maximum: 0.99)
    : BenchmarkConfiguration.SlidingWindow.CompactionThreshold ?? 0.80;
var maxContentCharacters = ReadInt("Max failure preview chars", 1600, minimum: 200);
var resultsDirectory = ReadText("Results directory", Path.Combine(AppContext.BaseDirectory, "results"));

var configurations = CreateConfigurations(selectedModes, maxIterations, maxTokens, compactionThreshold);

Console.WriteLine();
Console.WriteLine($"Tasks: {string.Join(", ", selectedTasks.Select(static task => task.Name))}");
Console.WriteLine($"Configurations: {string.Join(", ", configurations.Select(DescribeConfiguration))}");
Console.WriteLine($"Results: {resultsDirectory}");
Console.WriteLine();

var runner = new BenchmarkRunner();
var reportWriter = new JsonReportWriter();

foreach (var task in selectedTasks)
{
    Console.WriteLine($"Running task: {task.Name}");

    var report = await runner.RunAsync(task, configurations);
    var reportPath = await reportWriter.WriteAsync(report, resultsDirectory);

    WriteReport(report, reportPath, maxContentCharacters);
    Console.WriteLine();
}

Console.WriteLine($"Completed {selectedTasks.Count} benchmark(s).");
Console.WriteLine($"Reports written to: {resultsDirectory}");

return;

static bool SelectRunAll()
{
    Console.Write("Run all tasks? [y/N]: ");
    var input = Console.ReadLine();
    Console.WriteLine();

    return string.Equals(input?.Trim(), "y", StringComparison.OrdinalIgnoreCase)
           || string.Equals(input?.Trim(), "yes", StringComparison.OrdinalIgnoreCase);
}

static AgentLoopTaskDefinition SelectTask(IReadOnlyList<AgentLoopTaskDefinition> tasks)
{
    Console.WriteLine("Select task:");

    for (var i = 0; i < tasks.Count; i++)
    {
        Console.WriteLine($"{i + 1}. {tasks[i].Name}");
    }

    Console.Write("Choice [1]: ");
    var input = Console.ReadLine();
    Console.WriteLine();

    return int.TryParse(input, out var index) && index >= 1 && index <= tasks.Count
        ? tasks[index - 1]
        : tasks[0];
}

static IReadOnlyList<BenchmarkMode> SelectBenchmarkModes()
{
    Console.WriteLine("Select benchmark config:");
    Console.WriteLine("1. Raw only");
    Console.WriteLine("2. SlidingWindow only");
    Console.WriteLine("3. Both");
    Console.Write("Choice [3]: ");
    var input = Console.ReadLine();
    Console.WriteLine();

    return input?.Trim() switch
    {
        "1" => [BenchmarkMode.Raw],
        "2" => [BenchmarkMode.SlidingWindow],
        _ => [BenchmarkMode.Raw, BenchmarkMode.SlidingWindow],
    };
}

static IReadOnlyList<BenchmarkConfiguration> CreateConfigurations(
    IReadOnlyList<BenchmarkMode> selectedModes,
    int maxIterations,
    int maxTokens,
    double compactionThreshold)
{
    List<BenchmarkConfiguration> configurations = [];

    foreach (var mode in selectedModes)
    {
        switch (mode)
        {
            case BenchmarkMode.Raw:
                configurations.Add(new BenchmarkConfiguration(
                    Name: BenchmarkConfiguration.Raw.Name,
                    Mode: BenchmarkConfiguration.Raw.Mode,
                    MaxTokens: null,
                    CompactionThreshold: null,
                    MaxIterations: maxIterations));
                break;
            case BenchmarkMode.SlidingWindow:
                configurations.Add(new BenchmarkConfiguration(
                    Name: BenchmarkConfiguration.SlidingWindow.Name,
                    Mode: BenchmarkConfiguration.SlidingWindow.Mode,
                    MaxTokens: maxTokens,
                    CompactionThreshold: compactionThreshold,
                    MaxIterations: maxIterations));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(selectedModes), mode, "Unsupported benchmark mode.");
        }
    }

    return configurations;
}

static void WriteReport(BenchmarkReport report, string reportPath, int maxContentCharacters)
{
    Console.WriteLine($"Task: {report.Task}");
    Console.WriteLine($"Model: {report.Model}");

    foreach (var run in report.Runs)
    {
        Console.WriteLine($"- {run.Configuration}: completed={run.Completed}, turns={run.TurnCount}, input={run.TotalInputTokens}, output={run.TotalOutputTokens}, compactions={run.CompactionEvents}, durationMs={run.DurationMs}");

        if (!string.IsNullOrWhiteSpace(run.FailureReason))
        {
            Console.WriteLine($"  Failure: {TrimForDisplay(run.FailureReason, maxContentCharacters)}");
        }
    }

    if (report.Comparison is not null)
    {
        Console.WriteLine($"Comparison: savings={report.Comparison.InputTokenSavingsPercent:0.0}%, rawInput={report.Comparison.TotalInputTokensRaw}, managedInput={report.Comparison.TotalInputTokensManaged}, bothCompleted={report.Comparison.BothCompleted}");
    }
    else
    {
        Console.WriteLine("Comparison: n/a");
    }

    Console.WriteLine($"Report: {reportPath}");
}

static int ReadInt(string label, int defaultValue, int minimum)
{
    Console.Write($"{label} [{defaultValue}]: ");
    var input = Console.ReadLine();
    Console.WriteLine();

    return int.TryParse(input, out var value) && value >= minimum
        ? value
        : defaultValue;
}

static double ReadDouble(string label, double defaultValue, double minimum, double maximum)
{
    Console.Write($"{label} [{defaultValue:0.00}]: ");
    var input = Console.ReadLine();
    Console.WriteLine();

    return double.TryParse(input, out var value) && value >= minimum && value <= maximum
        ? value
        : defaultValue;
}

static string ReadText(string label, string defaultValue)
{
    Console.Write($"{label} [{defaultValue}]: ");
    var input = Console.ReadLine();
    Console.WriteLine();

    return string.IsNullOrWhiteSpace(input)
        ? defaultValue
        : input.Trim();
}

static string DescribeConfiguration(BenchmarkConfiguration configuration)
{
    return configuration.Mode == BenchmarkMode.Raw
        ? $"{configuration.Name} (iterations={configuration.MaxIterations})"
        : $"{configuration.Name} (maxTokens={configuration.MaxTokens}, threshold={configuration.CompactionThreshold:0.00}, iterations={configuration.MaxIterations})";
}

static string TrimForDisplay(string value, int maxLength)
{
    return value.Length <= maxLength
        ? value
        : string.Concat(value.AsSpan(0, maxLength), "...");
}
