using OpenAI.Chat;
using TokenGuard.Benchmark.AgentWorkflow;
using TokenGuard.Benchmark.AgentWorkflow.Models;
using TokenGuard.Benchmark.AgentWorkflow.Tasks;
using TokenGuard.Benchmark.Reporting;
using TokenGuard.Benchmark.Retention;
using TokenGuard.Core.Contexts;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;
using TokenGuard.Core.Options;
using TokenGuard.Core.Strategies;
using TokenGuard.Core.TokenCounting;
using TokenGuard.Extensions.OpenAI;

Console.WriteLine("TokenGuard Benchmark Runner");
Console.WriteLine();

var layer = SelectLayer();

switch (layer)
{
    case BenchmarkLayer.Retention:
        await RunRetentionAsync();
        break;

    case BenchmarkLayer.Continuity:
        RunContinuityPlaceholder();
        break;

    case BenchmarkLayer.AgentWorkflow:
        await RunAgentWorkflowAsync();
        break;

    default:
        throw new ArgumentOutOfRangeException(nameof(layer), layer, "Unsupported benchmark layer.");
}

return;

static BenchmarkLayer SelectLayer()
{
    var layers = new[]
    {
        BenchmarkLayer.Retention,
        BenchmarkLayer.Continuity,
        BenchmarkLayer.AgentWorkflow,
    };

    Console.WriteLine("Select layer:");

    for (var i = 0; i < layers.Length; i++)
    {
        Console.WriteLine($"{i + 1}. {GetLayerDisplayName(layers[i])}");
    }

    Console.Write("Choice [1]: ");
    var input = Console.ReadLine();
    Console.WriteLine();

    if (string.IsNullOrWhiteSpace(input))
    {
        return layers[0];
    }

    return int.TryParse(input, out var index) && index >= 1 && index <= layers.Length
        ? layers[index - 1]
        : layers[0];
}

static async Task RunRetentionAsync()
{
    var mode = SelectRetentionRunMode();
    var profiles = mode == RetentionRunMode.All
        ? BuiltInRetentionProfiles.All()
        : [SelectRetentionProfile()];

    var tokenCounter = new EstimatedTokenCounter();
    var runner = new RetentionBenchmarkRunner(
        tokenCounter,
        InvokeRetentionLlmAsync,
        () => new ConversationContext(
            new ContextBudget(12_000, 0.80, 1.0, 0),
            tokenCounter,
            strategy: new SlidingWindowStrategy(),
            observer: null));

    if (mode == RetentionRunMode.All)
    {
        var reports = await runner.RunAllAsync(profiles);
        WriteRetentionBatchSummary(reports);
        return;
    }

    var report = await runner.RunAsync(profiles[0]);
    WriteRetentionReport(report);
}

static RetentionRunMode SelectRetentionRunMode()
{
    var modes = new[]
    {
        RetentionRunMode.Single,
        RetentionRunMode.All,
    };

    Console.WriteLine("Retention:");
    Console.WriteLine("Select run scope:");

    for (var i = 0; i < modes.Length; i++)
    {
        Console.WriteLine($"{i + 1}. {GetRetentionRunModeDisplayName(modes[i])}");
    }

    Console.Write("Choice [1]: ");
    var input = Console.ReadLine();
    Console.WriteLine();

    if (string.IsNullOrWhiteSpace(input))
    {
        return modes[0];
    }

    return int.TryParse(input, out var index) && index >= 1 && index <= modes.Length
        ? modes[index - 1]
        : modes[0];
}

static ScenarioProfile SelectRetentionProfile()
{
    var profiles = BuiltInRetentionProfiles.All();

    Console.WriteLine("Select profile:");

    for (var i = 0; i < profiles.Count; i++)
    {
        var profile = profiles[i];
        Console.WriteLine($"{i + 1}. {profile.Name} ({profile.TargetTokenCount:N0} tokens, {profile.TurnCount} turns, {profile.Facts.Count} facts)");
    }

    Console.Write("Choice [1]: ");
    var input = Console.ReadLine();
    Console.WriteLine();

    if (string.IsNullOrWhiteSpace(input))
    {
        return profiles[0];
    }

    return int.TryParse(input, out var index) && index >= 1 && index <= profiles.Count
        ? profiles[index - 1]
        : profiles[0];
}

static async Task<string> InvokeRetentionLlmAsync(IReadOnlyList<ContextMessage> messages, string recallProbe)
{
    var allMessages = new List<ChatMessage>(messages.Count + 1);

    foreach (var message in messages)
    {
        allMessages.Add(ConvertToChatMessage(message));
    }

    allMessages.Add(new UserChatMessage(recallProbe));

    var client = OpenRouterE2ETestSupport.CreateChatClient(BenchmarkRunner.ModelName);
    var completion = (await client.CompleteChatAsync(allMessages)).Value;
    return string.Join(Environment.NewLine, completion.TextSegments().Select(static segment => segment.Content));
}

static ChatMessage ConvertToChatMessage(ContextMessage message)
{
    if (message.Segments is not [TextContent text])
    {
        throw new InvalidOperationException("Retention benchmark currently supports text-only synthetic messages.");
    }

    return message.Role switch
    {
        MessageRole.System => new SystemChatMessage(text.Content),
        MessageRole.User => new UserChatMessage(text.Content),
        MessageRole.Model => new AssistantChatMessage(text.Content),
        _ => throw new InvalidOperationException($"Unsupported message role '{message.Role}' for retention benchmark."),
    };
}

static void WriteRetentionReport(RetentionBenchmarkReport report)
{
    Console.WriteLine($"Profile: {report.ProfileName}");
    Console.WriteLine($"Baseline retention: {report.Baseline.RetentionScore:P1}");
    Console.WriteLine($"Managed retention: {report.Managed.RetentionScore:P1}");
    Console.WriteLine($"Retention delta: {report.RetentionDelta:+0.0%;-0.0%;0.0%}");
    Console.WriteLine($"Token savings: {report.TokenSavingsPercent:P1}");
}

static void WriteRetentionBatchSummary(IReadOnlyList<RetentionBenchmarkReport> reports)
{
    Console.WriteLine("Retention batch summary:");

    foreach (var report in reports)
    {
        Console.WriteLine($"- {report.ProfileName}: baseline={report.Baseline.RetentionScore:P1}, managed={report.Managed.RetentionScore:P1}, delta={report.RetentionDelta:+0.0%;-0.0%;0.0%}, savings={report.TokenSavingsPercent:P1}");
    }
}

static void RunContinuityPlaceholder()
{
    Console.WriteLine("Continuity benchmark layer is planned but not implemented yet.");
    Console.WriteLine("Use Retention for Layer 1 runs or Agent Workflow for the current Layer 3 transition path.");
}

static async Task RunAgentWorkflowAsync()
{
    var mode = SelectAgentWorkflowRunMode();
    var configurations = new[]
    {
        BenchmarkConfiguration.Raw,
        BenchmarkConfiguration.SlidingWindow,
    };

    var runner = new BenchmarkRunner();
    var reportWriter = new JsonReportWriter();
    var resultsDirectory = Path.Combine(AppContext.BaseDirectory, "results");

    if (mode == AgentWorkflowRunMode.All)
    {
        var tasks = BuiltInAgentLoopTasks.All();
        var reportPaths = new List<string>(tasks.Count);

        foreach (var task in tasks)
        {
            var report = await runner.RunAsync(task, configurations);
            var reportPath = await reportWriter.WriteAsync(report, resultsDirectory);
            reportPaths.Add(reportPath);
            WriteAgentWorkflowReport(report, reportPath);
            Console.WriteLine();
        }

        Console.WriteLine($"Completed {tasks.Count} agent workflow benchmarks.");
        return;
    }

    var selectedTask = SelectAgentWorkflowTask();
    var singleReport = await runner.RunAsync(selectedTask, configurations);
    var singleReportPath = await reportWriter.WriteAsync(singleReport, resultsDirectory);
    WriteAgentWorkflowReport(singleReport, singleReportPath);
}

static AgentWorkflowRunMode SelectAgentWorkflowRunMode()
{
    var modes = new[]
    {
        AgentWorkflowRunMode.Single,
        AgentWorkflowRunMode.All,
    };

    Console.WriteLine("Agent Workflow:");
    Console.WriteLine("Select run scope:");

    for (var i = 0; i < modes.Length; i++)
    {
        Console.WriteLine($"{i + 1}. {GetAgentWorkflowRunModeDisplayName(modes[i])}");
    }

    Console.Write("Choice [1]: ");
    var input = Console.ReadLine();
    Console.WriteLine();

    if (string.IsNullOrWhiteSpace(input))
    {
        return modes[0];
    }

    return int.TryParse(input, out var index) && index >= 1 && index <= modes.Length
        ? modes[index - 1]
        : modes[0];
}

static AgentLoopTaskDefinition SelectAgentWorkflowTask()
{
    var tasks = BuiltInAgentLoopTasks.All();

    Console.WriteLine("Select task:");

    for (var i = 0; i < tasks.Count; i++)
    {
        Console.WriteLine($"{i + 1}. {tasks[i].Name}");
    }

    Console.Write("Choice [1]: ");
    var input = Console.ReadLine();
    Console.WriteLine();

    if (string.IsNullOrWhiteSpace(input))
    {
        return tasks[0];
    }

    return int.TryParse(input, out var index) && index >= 1 && index <= tasks.Count
        ? tasks[index - 1]
        : tasks[0];
}

static void WriteAgentWorkflowReport(BenchmarkReport report, string reportPath)
{
    Console.WriteLine($"Task: {report.Task}");
    Console.WriteLine($"Model: {report.Model}");
    Console.WriteLine($"Raw input tokens: {report.Comparison.TotalInputTokensRaw}");
    Console.WriteLine($"SlidingWindow input tokens: {report.Comparison.TotalInputTokensManaged}");
    Console.WriteLine($"Savings: {report.Comparison.InputTokenSavingsPercent}%");
    Console.WriteLine($"Both completed: {report.Comparison.BothCompleted}");
    Console.WriteLine($"Report: {reportPath}");
}

static string GetLayerDisplayName(BenchmarkLayer layer) => layer switch
{
    BenchmarkLayer.Retention => "Retention (Layer 1)",
    BenchmarkLayer.Continuity => "Continuity (Layer 2)",
    BenchmarkLayer.AgentWorkflow => "Agent Workflow (Layer 3)",
    _ => throw new ArgumentOutOfRangeException(nameof(layer), layer, "Unsupported benchmark layer."),
};

static string GetRetentionRunModeDisplayName(RetentionRunMode mode) => mode switch
{
    RetentionRunMode.Single => "Single profile",
    RetentionRunMode.All => "All built-in profiles",
    _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported retention run mode."),
};

static string GetAgentWorkflowRunModeDisplayName(AgentWorkflowRunMode mode) => mode switch
{
    AgentWorkflowRunMode.Single => "Single task",
    AgentWorkflowRunMode.All => "All built-in tasks",
    _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported agent workflow run mode."),
};

enum BenchmarkLayer
{
    Retention,
    Continuity,
    AgentWorkflow,
}

enum RetentionRunMode
{
    Single,
    All,
}

enum AgentWorkflowRunMode
{
    Single,
    All,
}
