using Microsoft.Extensions.Configuration;
using OpenAI;
using OpenAI.Chat;
using TokenGuard.Core;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;
using TokenGuard.Core.Options;
using TokenGuard.Core.Strategies;
using TokenGuard.Core.TokenCounting;
using TokenGuard.Extensions.OpenAI;
using TokenGuard.Tools.Tools;

namespace TokenGuard.Samples.Console.AgentLoops;

using Console = System.Console;

public sealed class CompleteAgentLoop : IAgentLoop
{
    public string Name => "Complete OpenAI loop";

    public async Task RunAsync()
    {
        var startTime = DateTime.Now;
        var tasksDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Tasks");

        Console.WriteLine("=========================================");
        Console.WriteLine("   TokenGuard.Core Agentic Loop Sample");
        Console.WriteLine("=========================================\n");

        using var logger = new SessionLogger();

        var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";

        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();

        var apiKey = configuration["OpenRouterAPIKey"] ?? Environment.GetEnvironmentVariable("OPENROUTER_API_KEY") ?? "sk-test-key";

        var endpoint = new Uri("https://openrouter.ai/api/v1");
        var client = new OpenAIClient(new System.ClientModel.ApiKeyCredential(apiKey), new OpenAIClientOptions { Endpoint = endpoint });

        var chatClient = client.GetChatClient("qwen/qwen3.6-plus");

        var budget = ContextBudget.For(maxTokens: 10000);
        var counter = new EstimatedTokenCounter();
        var strategy = new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 4));
        var conversationContext = new ConversationContext(budget, counter, strategy);

        logger.LogBudgetInfo(budget, nameof(SlidingWindowStrategy));

        var tools = CreateTools(Directory.GetCurrentDirectory());
        var chatTools = tools.Select(t => t.Name switch
        {
            "list_files" => ChatTool.CreateFunctionTool(
                functionName: t.Name,
                functionDescription: t.Description),
            _ => ChatTool.CreateFunctionTool(
                functionName: t.Name,
                functionDescription: t.Description,
                functionParameters: BinaryData.FromString(t.ParametersSchema!.RootElement.GetRawText())),
        }).ToList();

        var chatOptions = new ChatCompletionOptions();
        foreach (var tool in chatTools)
        {
            chatOptions.Tools.Add(tool);
        }

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

            var compactionTriggered = preparedMessages.Any(m => m.State != CompactionState.Original);
            if (compactionTriggered)
            {
                totalCompactionCount++;
            }

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"[TokenGuard.Core: Prepared {preparedMessages.Count} messages for LLM]");
            Console.ResetColor();

            var openAiMessages = preparedMessages.ForOpenAI();

            ChatCompletion response;
            try
            {
                response = await chatClient.CompleteChatAsync(openAiMessages, chatOptions);
            }
            catch (Exception ex)
            {
                logger.LogError("Chat Completion", ex);
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"API Error: {ex.Message}");
                Console.ResetColor();
                break;
            }

            int? inputTokens = response.Usage?.InputTokenCount;

            if (response.FinishReason == ChatFinishReason.ToolCalls)
            {
                var contentBlocks = new List<ContentSegment>();
                var responseText = response.Content.Count > 0 ? response.Content[0].Text : string.Empty;

                if (!string.IsNullOrWhiteSpace(responseText))
                {
                    contentBlocks.Add(new TextContent(responseText));
                    Console.WriteLine($"Agent: {responseText}");
                }

                foreach (var call in response.ToolCalls)
                {
                    contentBlocks.Add(new ToolUseContent(call.Id, call.FunctionName, call.FunctionArguments.ToString()));
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"[Agent calls tool: {call.FunctionName}({call.FunctionArguments})]");
                    Console.ResetColor();
                }

                conversationContext.RecordModelResponse(contentBlocks, inputTokens);

                var recordedMsg = conversationContext.History.Last();
                var toolCallNames = string.Join(", ", contentBlocks.OfType<ToolUseContent>().Select(t => t.ToolName));
                logger.LogModelResponse(recordedMsg, inputTokens, $"Tool Calls [{toolCallNames}]");

                foreach (var call in response.ToolCalls)
                {
                    var resultText = toolMap.TryGetValue(call.FunctionName, out var tool)
                        ? tool.Execute(call.FunctionArguments.ToString())
                        : "Error: Unknown tool.";

                    Console.ForegroundColor = ConsoleColor.DarkGreen;
                    Console.WriteLine($"[Tool Result: {resultText.Length} chars]");
                    Console.ResetColor();

                    conversationContext.RecordToolResult(call.Id, call.FunctionName, resultText);
                    logger.LogToolResultRecorded(conversationContext.History.Last());
                }

                continue;
            }

            var finalResponseText = response.Content.Count > 0 ? response.Content[0].Text : "[No Content]";
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"Agent: {finalResponseText}\n");
            Console.ResetColor();

            conversationContext.RecordModelResponse([new TextContent(finalResponseText)], inputTokens);
            logger.LogModelResponse(conversationContext.History.Last(), inputTokens, "Text Response");

            taskCompleted = finalResponseText.Contains("TASK_COMPLETE", StringComparison.Ordinal);

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
        logger.LogSessionSummary(conversationContext.History.Count, totalCompactionCount, duration, finalTokenCount);

        Console.WriteLine($"\nSession complete. Log file: {logger.LogFilePath}");
    }

    private static ITool[] CreateTools(string workspaceDirectory) =>
    [
        new ListFilesTool(workspaceDirectory),
        new ReadFileTool(workspaceDirectory),
        new CreateTextFileTool(workspaceDirectory),
        new EditTextFileTool(workspaceDirectory),
    ];
}
