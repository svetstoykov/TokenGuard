using Microsoft.Extensions.Configuration;
using TokenGuard.Core;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;
using TokenGuard.Core.Options;
using TokenGuard.Core.Strategies;
using TokenGuard.Core.TokenCounting;
using TokenGuard.Samples.Console.AgentLoops.Providers;
using TokenGuard.Tools.Tools;

namespace TokenGuard.Samples.Console.AgentLoops;

using Console = System.Console;

public sealed class ProviderSwappableAgentLoop
{
    private const string SystemPrompt =
        "You are an autonomous AI agent running inside a sample agent loop. " +
        "You must fully complete the assigned task using the available tools when needed. " +
        "Do not ask the user for clarification, status updates, or permission. " +
        "Work iteratively until the task is complete. " +
        "When and only when the task is fully complete, respond with a concise final report that ends with the exact line TASK_COMPLETE. " +
        "If more work remains, continue working instead of stopping. " +
        "You have tools to list files, read files, create text files, and edit text files in the current directory. " +
        "Keep intermediate responses concise.";

    private const string ContinuationPrompt =
        "The task is not finished yet. Continue working until it is complete. " +
        "Only your final completion message may end with the exact line TASK_COMPLETE.";

    public async Task RunAsync(AgentLoopOptions options)
    {
        var startTime = DateTime.Now;
        var workspaceDirectory = Directory.GetCurrentDirectory();
        var tasksDirectory = Path.Combine(workspaceDirectory, "Tasks");

        using var logger = new SessionLogger();

        var configuration = BuildConfiguration();
        var provider = CreateProvider(options, configuration);

        var budget = ContextBudget.For(maxTokens: 10000);
        var counter = new EstimatedTokenCounter();
        var strategy = new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 10));
        var conversationContext = new ConversationContext(budget, counter, strategy);

        logger.LogBudgetInfo(budget, nameof(SlidingWindowStrategy));

        var tools = CreateTools(workspaceDirectory);

        Directory.CreateDirectory(tasksDirectory);

        var taskFilePath = Directory
            .GetFiles(tasksDirectory, "*.txt")
            .OrderBy(Path.GetFileName)
            .FirstOrDefault();

        if (taskFilePath is null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"No task files found in {tasksDirectory}.");
            Console.ResetColor();
            return;
        }

        var taskFileName = Path.GetFileName(taskFilePath);
        var taskText = await File.ReadAllTextAsync(taskFilePath);

        if (string.IsNullOrWhiteSpace(taskText))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Task file '{taskFileName}' is empty.");
            Console.ResetColor();
            return;
        }

        conversationContext.SetSystemPrompt(SystemPrompt);
        logger.LogMessageAdded(conversationContext.History.First(), "System Prompt Updated");

        Console.WriteLine("=========================================");
        Console.WriteLine("   TokenGuard.Core Agentic Loop Sample");
        Console.WriteLine("=========================================\n");
        Console.WriteLine($"Provider: {provider.Name}");
        Console.WriteLine($"Model: {provider.ModelId}");
        Console.WriteLine($"Loaded task: {taskFileName}");
        Console.WriteLine("Running autonomous task from Tasks folder.\n");

        var totalCompactionCount = 0;

        conversationContext.AddUserMessage($"Task file: {taskFileName}\n\n{taskText}");
        logger.LogMessageAdded(conversationContext.History.Last(), "Task Loaded");

        var toolMap = tools.ToDictionary(t => t.Name, t => t);
        var taskCompleted = false;

        while (!taskCompleted)
        {
            logger.LogHistoryBeforePrepare(conversationContext.History);

            var preparedMessages = await conversationContext.PrepareAsync();
            logger.LogPreparedMessages(preparedMessages, budget);

            if (preparedMessages.Any(m => m.State != CompactionState.Original))
            {
                totalCompactionCount++;
            }

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"[TokenGuard.Core: Prepared {preparedMessages.Count} messages for LLM]");
            Console.ResetColor();

            ProviderTurnResult turnResult;
            try
            {
                turnResult = await provider.ExecuteTurnAsync(preparedMessages, tools);
            }
            catch (Exception ex)
            {
                logger.LogError($"{provider.Name} turn execution", ex);
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"API Error: {ex.Message}");
                Console.ResetColor();
                break;
            }

            DisplayResponse(turnResult);

            conversationContext.RecordModelResponse(turnResult.ResponseSegments, turnResult.InputTokens);
            logger.LogModelResponse(
                conversationContext.History.Last(),
                turnResult.InputTokens,
                turnResult.HasToolCalls ? $"Tool Calls [{string.Join(", ", turnResult.ToolCalls.Select(call => call.ToolName))}]" : "Text Response");

            if (turnResult.HasToolCalls)
            {
                foreach (var call in turnResult.ToolCalls)
                {
                    var resultText = toolMap.TryGetValue(call.ToolName, out var tool)
                        ? tool.Execute(call.ArgumentsJson)
                        : "Error: Unknown tool.";

                    Console.ForegroundColor = ConsoleColor.DarkGreen;
                    Console.WriteLine($"[Tool Result: {resultText.Length} chars]");
                    Console.ResetColor();

                    conversationContext.RecordToolResult(call.ToolCallId, call.ToolName, resultText);
                    logger.LogToolResultRecorded(conversationContext.History.Last());
                }

                continue;
            }

            taskCompleted = turnResult.ResponseSegments
                .OfType<TextContent>()
                .Any(segment => segment.Content.Contains("TASK_COMPLETE", StringComparison.Ordinal));

            if (!taskCompleted)
            {
                conversationContext.AddUserMessage(ContinuationPrompt);
                logger.LogMessageAdded(conversationContext.History.Last(), "Continuation Prompt Added");
            }
        }

        var duration = DateTime.Now - startTime;
        var finalTokenCount = conversationContext.History.Sum(m => m.TokenCount ?? 0);
        logger.LogSessionSummary(conversationContext.History.Count, totalCompactionCount, duration, finalTokenCount);

        Console.WriteLine($"\nSession complete. Log file: {logger.LogFilePath}");
    }

    private static IConfiguration BuildConfiguration()
    {
        var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";

        return new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();
    }

    private static IAgentLoopProvider CreateProvider(AgentLoopOptions options, IConfiguration configuration)
        => options.Provider switch
        {
            ProviderKind.OpenRouter => new OpenRouterAgentLoopProvider(configuration, options),
            ProviderKind.Anthropic => new AnthropicAgentLoopProvider(configuration, options),
            _ => throw new ArgumentOutOfRangeException(nameof(options.Provider), options.Provider, "Unsupported provider."),
        };

    private static void DisplayResponse(ProviderTurnResult turnResult)
    {
        foreach (var text in turnResult.ResponseSegments.OfType<TextContent>())
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"Agent: {text.Content}\n");
            Console.ResetColor();
        }

        foreach (var call in turnResult.ToolCalls)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[Agent calls tool: {call.ToolName}({call.ArgumentsJson})]");
            Console.ResetColor();
        }
    }

    private static ITool[] CreateTools(string workspaceDirectory) =>
    [
        new ListFilesTool(workspaceDirectory),
        new ReadFileTool(workspaceDirectory),
        new CreateTextFileTool(workspaceDirectory),
        new EditTextFileTool(workspaceDirectory),
    ];
}
