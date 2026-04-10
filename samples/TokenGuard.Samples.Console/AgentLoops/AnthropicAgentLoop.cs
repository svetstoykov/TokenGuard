using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Configuration;
using TokenGuard.Core;
using TokenGuard.Core.Models;
using TokenGuard.Core.Options;
using TokenGuard.Core.Strategies;
using TokenGuard.Core.TokenCounting;
using TokenGuard.Extensions.Anthropic;
using TokenGuard.Tools.Tools;

namespace TokenGuard.Samples.Console.AgentLoops;

using Console = System.Console;

public sealed class AnthropicAgentLoop : IAgentLoop
{
    public string Name => "Minimal Anthropic loop";

    public async Task RunAsync()
    {
        var startTime = DateTime.Now;
        var tasksDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Tasks");

        using var logger = new SessionLogger();

        var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";

        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();

        var client = new AnthropicClient() { ApiKey = configuration["AnthropicAPIKey"] ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? "sk-test-key" };

        var budget = ContextBudget.For(maxTokens: 10000);
        var counter = new EstimatedTokenCounter();
        var strategy = new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 4));
        var conversationContext = new ConversationContext(budget, counter, strategy);

        logger.LogBudgetInfo(budget, nameof(SlidingWindowStrategy));

        var tools = CreateTools(Directory.GetCurrentDirectory());

        Directory.CreateDirectory(tasksDirectory);

        var taskFilePath = Directory
            .GetFiles(tasksDirectory, "*.txt")
            .OrderBy(Path.GetFileName)
            .FirstOrDefault();
        if (taskFilePath is null)
        {
            Console.WriteLine($"No task files found in {tasksDirectory}.");
            return;
        }

        var taskFileName = Path.GetFileName(taskFilePath);
        var taskText = await File.ReadAllTextAsync(taskFilePath);
        if (string.IsNullOrWhiteSpace(taskText))
        {
            Console.WriteLine($"Task file '{taskFileName}' is empty.");
            return;
        }

        conversationContext.SetSystemPrompt(
            "You are an autonomous AI agent running inside a sample agent loop. " +
            "You must fully complete the assigned task using the available tools when needed. " +
            "Do not ask the user for clarification, status updates, or permission. " +
            "Work iteratively until the task is complete. " +
            "When and only when the task is fully complete, respond with a concise final report that ends with the exact line TASK_COMPLETE. " +
            "If more work remains, continue working instead of stopping. " +
            "You have tools to list files, read files, create text files, and edit text files in the current directory. " +
            "Keep intermediate responses concise.");
        logger.LogMessageAdded(conversationContext.History.First(), "System Prompt Updated");

        conversationContext.AddUserMessage($"Task file: {taskFileName}\n\n{taskText}");
        logger.LogMessageAdded(conversationContext.History.Last(), "Task Loaded");

        var toolMap = tools.ToDictionary(t => t.Name, t => t);
        var anthropicTools = tools.Select(tool => (ToolUnion)ToAnthropicTool(tool)).ToList();

        var taskCompleted = false;
        while (!taskCompleted)
        {
            var preparedMessages = await conversationContext.PrepareAsync();
            var (messages, system) = preparedMessages.ForAnthropic();
            var parameters = new MessageCreateParams
            {
                Model = "claude-3-haiku-20240307",
                MaxTokens = 1024,
                Messages = messages,
                System = system,
                Tools = anthropicTools,
            };

            Message response;
            try
            {
                response = await client.Messages.Create(parameters);
            }
            catch (Exception ex)
            {
                logger.LogError("Anthropic Messages.Create", ex);
                break;
            }

            int? inputTokens = response.InputTokens();
            var contentSegments = response.ResponseSegments();

            conversationContext.RecordModelResponse(contentSegments, inputTokens);
            logger.LogModelResponse(
                conversationContext.History.Last(),
                inputTokens,
                response.ToolUseSegments().Count > 0 ? "Tool Calls" : "Text Response");

            var toolCalls = response.ToolUseSegments();
            if (toolCalls.Count > 0)
            {
                foreach (var call in toolCalls)
                {
                    var resultText = toolMap.TryGetValue(call.ToolName, out var tool)
                        ? tool.Execute(call.Content)
                        : "Error: Unknown tool.";

                    conversationContext.RecordToolResult(call.ToolCallId, call.ToolName, resultText);
                    logger.LogToolResultRecorded(conversationContext.History.Last());
                }

                continue;
            }

            var finalResponseText = response.TextSegments();
            taskCompleted = finalResponseText.Any(b => b.Content.Equals("TASK_COMPLETE", StringComparison.Ordinal));

            if (!taskCompleted)
            {
                conversationContext.AddUserMessage(
                    "The task is not finished yet. Continue working until it is complete. " +
                    "Only your final completion message may end with the exact line TASK_COMPLETE.");
                logger.LogMessageAdded(conversationContext.History.Last(), "Continuation Prompt Added");
            }
        }

        var duration = DateTime.Now - startTime;
        var finalTokenCount = conversationContext.History.Sum(m => m.TokenCount ?? 0);
        logger.LogSessionSummary(conversationContext.History.Count, totalCompactionCount: 0, duration, finalTokenCount);

        Console.WriteLine($"Session complete. Log file: {logger.LogFilePath}");
    }

    private static Tool ToAnthropicTool(ITool tool)
    {
        var properties = new Dictionary<string, JsonElement>();
        var required = Array.Empty<string>();

        if (tool.ParametersSchema is { } schema)
        {
            var root = schema.RootElement;

            if (root.TryGetProperty("properties", out var propsElement))
            {
                foreach (var prop in propsElement.EnumerateObject())
                    properties[prop.Name] = prop.Value.Clone();
            }

            if (root.TryGetProperty("required", out var reqElement))
            {
                required = reqElement.EnumerateArray()
                    .Select(static e => e.GetString())
                    .OfType<string>()
                    .ToArray();
            }
        }

        return new Tool
        {
            Name        = tool.Name,
            Description = tool.Description,
            InputSchema = new() { Properties = properties, Required = required },
        };
    }

    private static ITool[] CreateTools(string workspaceDirectory) =>
    [
        new ListFilesTool(workspaceDirectory),
        new ReadFileTool(workspaceDirectory),
        new CreateTextFileTool(workspaceDirectory),
        new EditTextFileTool(workspaceDirectory),
    ];
}
