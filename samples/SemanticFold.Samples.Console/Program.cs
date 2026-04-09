using OpenAI;
using OpenAI.Chat;
using SemanticFold.Abstractions;
using SemanticFold.Enums;
using SemanticFold.Models;
using SemanticFold.Models.Content;
using SemanticFold.Strategies;
using SemanticFold.TokenCounting;
using SemanticFold.Tools;

namespace SemanticFold.Samples.Console;

public static class Program{
    public static async Task Main()
    {
        System.Console.WriteLine("=========================================");
        System.Console.WriteLine("   SemanticFold Agentic Loop Sample");
        System.Console.WriteLine("=========================================\n");

        var apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY") ?? "sk-test-key";
        
        // We use OpenRouter as the API provider per request, falling back to OpenAI if no key is provided.
        var endpoint = new Uri("https://openrouter.ai/api/v1");
        var client = new OpenAIClient(new System.ClientModel.ApiKeyCredential(apiKey), new OpenAIClientOptions { Endpoint = endpoint });
        
        // The model string here should match your preferred OpenRouter or OpenAI model
        var chatClient = client.GetChatClient("openai/gpt-4o-mini");

        // 1. Initialize SemanticFold engine
        // Using a tiny budget (e.g., 500 tokens) to demonstrate compaction kicking in early.
        var budget = ContextBudget.For(maxTokens: 500); 
        var counter = new EstimatedTokenCounter();
        var strategy = new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 4));
        var foldEngine = new FoldingEngine(budget, counter, strategy);

        var history = new List<Message>();

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

        // Add a system prompt equivalent (using User role or Assistant as per your pattern)
        // We'll just append it as the first User message that sets context
        history.Add(Message.FromText(MessageRole.User, "System: You are a helpful AI assistant. You have tools to list and read files in the current directory. Keep responses concise."));

        System.Console.WriteLine("Assistant is ready! You can ask to list files or read a file.");
        System.Console.WriteLine("Type 'exit' to quit.\n");

        while (true)
        {
            System.Console.Write("User: ");
            var input = System.Console.ReadLine();
            
            if (string.IsNullOrWhiteSpace(input)) continue;
            if (input.Equals("exit", StringComparison.OrdinalIgnoreCase)) break;

            // Record user input
            history.Add(Message.FromText(MessageRole.User, input));

            // Agentic loop for tool calls
            while (true) 
            {
                // --- SEMANTICFOLD: Prepare the history ---
                // If history exceeds the budget, it will automatically apply the SlidingWindowStrategy.
                var preparedMessages = foldEngine.Prepare(history);

                // Print token usage for demonstration
                System.Console.ForegroundColor = ConsoleColor.DarkGray;
                System.Console.WriteLine($"[SemanticFold: Prepared {preparedMessages.Count} messages for LLM]");
                System.Console.ResetColor();

                // Convert to OpenAI format
                var openAiMessages = ConvertToOpenAiMessages(preparedMessages);

                ChatCompletion response;
                try
                {
                    response = await chatClient.CompleteChatAsync(openAiMessages, chatOptions);
                }
                catch (Exception ex)
                {
                    System.Console.ForegroundColor = ConsoleColor.Red;
                    System.Console.WriteLine($"API Error: {ex.Message}");
                    System.Console.ResetColor();
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
                        System.Console.WriteLine($"Agent: {responseText}");
                    }

                    foreach (var call in response.ToolCalls)
                    {
                        contentBlocks.Add(new ToolUseContent(call.Id, call.FunctionName, call.FunctionArguments.ToString()));
                        System.Console.ForegroundColor = ConsoleColor.Yellow;
                        System.Console.WriteLine($"[Agent calls tool: {call.FunctionName}({call.FunctionArguments})]");
                        System.Console.ResetColor();
                    }

                    var assistantMessage = new Message { Role = MessageRole.Model, Content = contentBlocks };
                    history.Add(assistantMessage);

                    // --- SEMANTICFOLD: Observe assistant message and anchor token count ---
                    foldEngine.Observe(assistantMessage, inputTokens);

                    // Execute all tools
                    var toolResultMessages = new List<Message>();
                    var toolMap = tools.ToDictionary(t => t.Name, t => t);
                    foreach (var call in response.ToolCalls)
                    {
                        var resultText = toolMap.TryGetValue(call.FunctionName, out var tool) 
                            ? tool.Execute(call.FunctionArguments.ToString())
                            : "Error: Unknown tool.";
                        
                        System.Console.ForegroundColor = ConsoleColor.DarkGreen;
                        System.Console.WriteLine($"[Tool Result: {resultText.Length} chars]");
                        System.Console.ResetColor();

                        var toolResultMessage = Message.FromContent(MessageRole.Tool, new ToolResultContent(call.Id, call.FunctionName, resultText));
                        toolResultMessages.Add(toolResultMessage);
                        history.Add(toolResultMessage);
                    }

                    // --- SEMANTICFOLD: Observe tool results ---
                    foldEngine.Observe(toolResultMessages, null);
                }
                else
                {
                    // Assistant replied directly
                    var responseText = response.Content.Count > 0 ? response.Content[0].Text : "[No Content]";
                    System.Console.ForegroundColor = ConsoleColor.Cyan;
                    System.Console.WriteLine($"Agent: {responseText}\n");
                    System.Console.ResetColor();

                    var assistantMessage = Message.FromText(MessageRole.Model, responseText);
                    history.Add(assistantMessage);

                    // --- SEMANTICFOLD: Observe final reply ---
                    foldEngine.Observe(assistantMessage, inputTokens);
                    
                    // Exit the tool loop and await next user input
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Translates SemanticFold <see cref="Message"/> items into the OpenAI SDK format.
    /// </summary>
    private static IEnumerable<ChatMessage> ConvertToOpenAiMessages(IReadOnlyList<Message> foldMessages)
    {
        var result = new List<ChatMessage>();
        
        foreach (var msg in foldMessages)
        {
            switch (msg.Role)
            {
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
}
