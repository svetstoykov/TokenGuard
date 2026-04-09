using Microsoft.Extensions.Configuration;
using OpenAI;
using OpenAI.Chat;
using SemanticFold.Extensions.OpenAI;
using SemanticFold.Core;
using SemanticFold.Core.Abstractions;
using SemanticFold.Core.Models;
using SemanticFold.Core.Models.Content;
using SemanticFold.Core.Strategies;
using SemanticFold.Core.TokenCounting;
using SemanticFold.Samples.Console.Tools;

namespace SemanticFold.Samples.Console;

public class MinimalAgentLoop
{
    public async Task Run()
    {
        var tasksDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Tasks");

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

        var tools = new ITool[]
        {
            new ListFilesTool(),
            new ReadFileTool(),
            new CreateTextFileTool(),
            new EditTextFileTool()
        };

        var chatTools = tools.Select(t => t.Name switch
        {
            "list_files" => ChatTool.CreateFunctionTool(
                functionName: t.Name,
                functionDescription: t.Description),
            _ => ChatTool.CreateFunctionTool(
                functionName: t.Name,
                functionDescription: t.Description,
                functionParameters: BinaryData.FromString(t.ParametersSchema!.RootElement.GetRawText()))
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
            "Keep intermediate responses concise."
        );

        conversationContext.AddUserMessage($"Task file: {taskFileName}\n\n{taskText}");

        var toolMap = tools.ToDictionary(t => t.Name, t => t);

        var taskCompleted = false;
        while (!taskCompleted)
        {
            var preparedMessages = conversationContext.Prepare();
            var openAiMessages = preparedMessages.ForOpenAI();

            ChatCompletion response;
            try
            {
                response = await chatClient.CompleteChatAsync(openAiMessages, chatOptions);
            }
            catch
            {
                break;
            }

            int? inputTokens = response.InputTokens();
            

            if (response.FinishReason == ChatFinishReason.ToolCalls)
            {
                var contentBlocks = new List<ContentBlock>();

                contentBlocks.AddRange(response.ResponseBlocks());
                
                conversationContext.RecordModelResponse(contentBlocks, inputTokens);

                foreach (var call in response.ToolCalls)
                {
                    var resultText = toolMap.TryGetValue(call.FunctionName, out var tool)
                        ? tool.Execute(call.FunctionArguments.ToString())
                        : "Error: Unknown tool.";

                    conversationContext.RecordToolResult(call.Id, call.FunctionName, resultText);
                }

                continue;
            }

            var finalResponseText = response.TextBlocks();

            conversationContext.RecordModelResponse(finalResponseText, inputTokens);

            taskCompleted = finalResponseText.Any(b => b.Text.Equals("TASK_COMPLETE", StringComparison.Ordinal));

            if (!taskCompleted)
            {
                conversationContext.AddUserMessage(
                    "The task is not finished yet. Continue working until it is complete. " +
                    "Only your final completion message may end with the exact line TASK_COMPLETE.");
            }
        }
    }
}