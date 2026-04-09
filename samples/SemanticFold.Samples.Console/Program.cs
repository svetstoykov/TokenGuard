using Microsoft.Extensions.Configuration;
using OpenAI;
using OpenAI.Chat;
using SemanticFold;
using SemanticFold.Abstractions;
using SemanticFold.Enums;
using SemanticFold.Models;
using SemanticFold.Models.Content;
using SemanticFold.Samples.Console.Tools;
using SemanticFold.Strategies;
using SemanticFold.TokenCounting;

Console.WriteLine("=========================================");
Console.WriteLine("   SemanticFold Agentic Loop Sample");
Console.WriteLine("=========================================\n");

var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables()
    .Build();

var apiKey = configuration["OpenRouterAPIKey"] ?? Environment.GetEnvironmentVariable("OPENROUTER_API_KEY") ?? "sk-test-key";

// We use OpenRouter as the API provider per request, falling back to OpenAI if no key is provided.
var endpoint = new Uri("https://openrouter.ai/api/v1");
var client = new OpenAIClient(new System.ClientModel.ApiKeyCredential(apiKey), new OpenAIClientOptions { Endpoint = endpoint });

// The model string here should match your preferred OpenRouter or OpenAI model
var chatClient = client.GetChatClient("qwen/qwen3.6-plus");

// 1. Initialize SemanticFold engine
// Using a tiny budget (e.g., 500 tokens) to demonstrate compaction kicking in early.
var budget = ContextBudget.For(maxTokens: 500);
var counter = new EstimatedTokenCounter();
var strategy = new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 4));
var foldEngine = new FoldingEngine(budget, counter, strategy);

// 2. Define Tools
var tools = new ITool[]
{
    new ListFilesTool(),
    new ReadFileTool()
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

// Set the system prompt
foldEngine.SetSystemPrompt("You are a helpful AI assistant. You have tools to list and read files in the current directory. Keep responses concise.");

Console.WriteLine("Assistant is ready! You can ask to list files or read a file.");
Console.WriteLine("Type 'exit' to quit.\n");

while (true)
{
    Console.Write("User: ");
    var input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input)) continue;
    if (input.Equals("exit", StringComparison.OrdinalIgnoreCase)) break;

    // Record user input
    foldEngine.AddUserMessage(input);

    // Agentic loop for tool calls
    while (true)
    {
        // --- SEMANTICFOLD: Prepare the history ---
        // If history exceeds the budget, it will automatically apply the SlidingWindowStrategy.
        var preparedMessages = foldEngine.Prepare();

        // Print token usage for demonstration
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"[SemanticFold: Prepared {preparedMessages.Count} messages for LLM]");
        Console.ResetColor();

        // Convert to OpenAI format
        var openAiMessages = ConvertToOpenAiMessages(preparedMessages);

        ChatCompletion response;
        try
        {
            response = await chatClient.CompleteChatAsync(openAiMessages, chatOptions);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"API Error: {ex.Message}");
            Console.ResetColor();
            break;
        }

        int? inputTokens = response.Usage?.InputTokenCount;

        if (response.FinishReason == ChatFinishReason.ToolCalls)
        {
            // Assistant requested a tool
            var contentBlocks = new List<ContentBlock>();
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

            // --- SEMANTICFOLD: Record assistant message and anchor token count ---
            foldEngine.RecordModelResponse(contentBlocks, inputTokens);

            // Execute all tools
            var toolMap = tools.ToDictionary(t => t.Name, t => t);
            foreach (var call in response.ToolCalls)
            {
                var resultText = toolMap.TryGetValue(call.FunctionName, out var tool)
                    ? tool.Execute(call.FunctionArguments.ToString())
                    : "Error: Unknown tool.";

                Console.ForegroundColor = ConsoleColor.DarkGreen;
                Console.WriteLine($"[Tool Result: {resultText.Length} chars]");
                Console.ResetColor();

                // --- SEMANTICFOLD: Record tool result ---
                foldEngine.RecordToolResult(call.Id, call.FunctionName, resultText);
            }
        }
        else
        {
            // Assistant replied directly
            var responseText = response.Content.Count > 0 ? response.Content[0].Text : "[No Content]";
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"Agent: {responseText}\n");
            Console.ResetColor();

            // --- SEMANTICFOLD: Record final reply ---
            foldEngine.RecordModelResponse([new TextContent(responseText)], inputTokens);

            // Exit the tool loop and await next user input
            break;
        }
    }
}


static IEnumerable<ChatMessage> ConvertToOpenAiMessages(IReadOnlyList<Message> foldMessages)
{
    var result = new List<ChatMessage>();

    foreach (var msg in foldMessages)
    {
        switch (msg.Role)
        {
            case MessageRole.System:
                var sysText = msg.Content.OfType<TextContent>().FirstOrDefault()?.Text ?? string.Empty;
                result.Add(new SystemChatMessage(sysText));
                break;

            case MessageRole.User:
                var userText = msg.Content.OfType<TextContent>().FirstOrDefault()?.Text ?? string.Empty;
                result.Add(new UserChatMessage(userText));
                break;

            case MessageRole.Model:
                var assistText = msg.Content.OfType<TextContent>().FirstOrDefault()?.Text ?? string.Empty;
                var assistantMsg = new AssistantChatMessage(assistText);

                foreach (var toolUse in msg.Content.OfType<ToolUseContent>())
                {
                    assistantMsg.ToolCalls.Add(ChatToolCall.CreateFunctionToolCall(
                        toolUse.ToolCallId,
                        toolUse.ToolName,
                        BinaryData.FromString(toolUse.ArgumentsJson)
                    ));
                }

                result.Add(assistantMsg);
                break;

            case MessageRole.Tool:
                var toolResult = msg.Content.OfType<ToolResultContent>().FirstOrDefault();
                if (toolResult != null)
                {
                    result.Add(new ToolChatMessage(toolResult.ToolCallId, toolResult.Content));
                }

                break;
        }
    }

    return result;
}