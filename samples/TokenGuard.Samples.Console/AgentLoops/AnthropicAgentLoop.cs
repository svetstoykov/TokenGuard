using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Configuration;
using TokenGuard.Core;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Models;
using TokenGuard.Core.Options;
using TokenGuard.Core.Strategies;
using TokenGuard.Core.TokenCounting;
using TokenGuard.Extensions.Anthropic;
using TokenGuard.TestCommon.Tools;

namespace TokenGuard.Samples.Console.AgentLoops;

public sealed class AnthropicAgentLoop : IAgentLoop
{
    public string Name => "Minimal Anthropic loop";

    public async Task RunAsync()
    {
        var tasksDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Tasks");

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

        var tools = CreateTools(Directory.GetCurrentDirectory());

        Directory.CreateDirectory(tasksDirectory);

        var taskFilePath = Directory
            .GetFiles(tasksDirectory, "*.txt")
            .OrderBy(Path.GetFileName)
            .FirstOrDefault();
        if (taskFilePath is null)
        {
            return;
        }

        var taskFileName = Path.GetFileName(taskFilePath);

        var taskText = await File.ReadAllTextAsync(taskFilePath);
        if (string.IsNullOrWhiteSpace(taskText))
        {
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

        conversationContext.AddUserMessage($"Task file: {taskFileName}\n\n{taskText}");

        var toolMap = tools.ToDictionary(t => t.Name, t => t);

        var taskCompleted = false;
        while (!taskCompleted)
        {
            var preparedMessages = await conversationContext.PrepareAsync();
            var parameters = preparedMessages.ForAnthropic(model: "claude-opus-4-6", maxTokens: 1024);

            // Tool registration with the Anthropic SDK is not documented in the provided guide.
            // Leave the request as message-only until the intended tool wiring is confirmed.

            Message response;
            try
            {
                response = await client.Messages.Create(parameters);
            }
            catch
            {
                break;
            }

            int? inputTokens = response.InputTokens();
            var contentSegments = response.ResponseSegments();

            conversationContext.RecordModelResponse(contentSegments, inputTokens);

            var toolCalls = response.ToolUseSegments();
            if (toolCalls.Count > 0)
            {
                foreach (var call in toolCalls)
                {
                    var resultText = toolMap.TryGetValue(call.ToolName, out var tool)
                        ? tool.Execute(call.ArgumentsJson)
                        : "Error: Unknown tool.";

                    conversationContext.RecordToolResult(call.ToolCallId, call.ToolName, resultText);
                }

                continue;
            }

            var finalResponseText = response.TextSegments();

            taskCompleted = finalResponseText.Any(b => b.Text.Equals("TASK_COMPLETE", StringComparison.Ordinal));

            if (!taskCompleted)
            {
                conversationContext.AddUserMessage(
                    "The task is not finished yet. Continue working until it is complete. " +
                    "Only your final completion message may end with the exact line TASK_COMPLETE.");
            }
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
