using TokenGuard.E2E.Tasks;
using TokenGuard.Samples.Benchmark;
using TokenGuard.Samples.Benchmark.Models;
using TokenGuard.Samples.Benchmark.Reporting;

var task = SelectTask();
var runner = new BenchmarkRunner();
var reportWriter = new JsonReportWriter();
var configurations = new[]
{
    BenchmarkConfiguration.Raw,
    BenchmarkConfiguration.SlidingWindow,
};

var report = await runner.RunAsync(task, configurations);
var resultsDirectory = Path.Combine(AppContext.BaseDirectory, "results");
var reportPath = await reportWriter.WriteAsync(report, resultsDirectory);

Console.WriteLine();
Console.WriteLine($"Task: {report.Task}");
Console.WriteLine($"Model: {report.Model}");
Console.WriteLine($"Raw input tokens: {report.Comparison.TotalInputTokensRaw}");
Console.WriteLine($"SlidingWindow input tokens: {report.Comparison.TotalInputTokensManaged}");
Console.WriteLine($"Savings: {report.Comparison.InputTokenSavingsPercent}%");
Console.WriteLine($"Both completed: {report.Comparison.BothCompleted}");
Console.WriteLine($"Report: {reportPath}");

static AgentLoopTaskDefinition SelectTask()
{
    var tasks = BuiltInAgentLoopTasks.All();

    Console.WriteLine("TokenGuard Benchmark Runner");
    Console.WriteLine("Select task:");

    for (var i = 0; i < tasks.Count; i++)
    {
        Console.WriteLine($"{i + 1}. {tasks[i].Name}");
    }

    Console.Write("Choice [1]: ");
    var input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input))
    {
        return tasks[0];
    }

    return int.TryParse(input, out var index) && index >= 1 && index <= tasks.Count
        ? tasks[index - 1]
        : tasks[0];
}
