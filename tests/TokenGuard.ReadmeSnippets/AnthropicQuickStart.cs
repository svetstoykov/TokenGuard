// <AnthropicUsings>
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.DependencyInjection;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Extensions;
using TokenGuard.Extensions.Anthropic;
// </AnthropicUsings>

namespace TokenGuard.ReadmeSnippets;

/// <summary>
///     Holds the Anthropic quick start so that the snippet check compiles it against the packed packages.
/// </summary>
/// <remarks>
///     Each tagged block is the text of a README code fence, indented here by the method body. verify.sh removes that
///     indentation and fails when a README stops carrying a block verbatim. The method is compiled and never called.
/// </remarks>
internal static class AnthropicQuickStart
{
    /// <summary>
    ///     Asynchronously runs the Anthropic quick start as a reader would after pasting it into a program.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public static async Task RunAsync()
    {
        // <AnthropicSetup>
        AnthropicClient anthropicClient = new(); // Reads the ANTHROPIC_API_KEY environment variable.
        var cancellationToken = CancellationToken.None;

        var services = new ServiceCollection();
        services.AddConversationContext(builder => builder
            .WithMaxTokens(40_000)
            .UseLlmSummarization(anthropicClient, "claude-opus-5-5"));

        using var serviceProvider = services.BuildServiceProvider();
        using var conversationContext = serviceProvider
            .GetRequiredService<IConversationContextFactory>()
            .Create();

        Tool readFile = new()
        {
            Name = "read_file",
            Description = "Reads a text file from the repository.",
            InputSchema = new()
            {
                Properties = new Dictionary<string, JsonElement>
                {
                    ["path"] = JsonSerializer.SerializeToElement(new { type = "string", description = "Path from the repository root." }),
                },
                Required = ["path"],
            },
        };
        // </AnthropicSetup>

        // <AnthropicLoop>
        conversationContext.SetSystemPrompt("You are a precise coding assistant.");
        conversationContext.AddPinnedMessage(MessageRole.User, "Repository root is /workspace/project.");
        conversationContext.AddUserMessage("Summarize the failing tests.");

        const int maxTurns = 20;

        for (var turn = 1; turn <= maxTurns; turn++)
        {
            var prepared = await conversationContext.PrepareAsync(cancellationToken);

            // Both outcomes mean the prepared request is still over the budget, so stop before the provider rejects it.
            if (prepared.Outcome is PrepareOutcome.CompactionInsufficient or PrepareOutcome.CannotCompact)
                throw new InvalidOperationException(prepared.BudgetFailureReason);

            var (messages, systemPrompt) = prepared.Messages.ForAnthropic();

            Message response = await anthropicClient.Messages.Create(
                new MessageCreateParams
                {
                    Model = "claude-opus-5-5",
                    MaxTokens = 16_000,
                    System = systemPrompt,
                    Messages = messages,
                    Tools = [readFile],
                },
                cancellationToken);
            var segments = response.ResponseSegments();

            // A reply with no text and no tool call cannot be recorded: RecordModelResponse throws for it.
            if (segments.Count == 0)
                throw new InvalidOperationException("The model returned an empty reply.");

            conversationContext.RecordModelResponse(segments, response.InputTokens());

            var toolCalls = response.ToolUseSegments();

            if (toolCalls.Count == 0)
            {
                Console.WriteLine(string.Join(Environment.NewLine, response.TextSegments().Select(segment => segment.Content)));
                return;
            }

            foreach (var toolCall in toolCalls)
            {
                var result = ExecuteTool(toolCall.ToolName, toolCall.Content);
                conversationContext.RecordToolResult(toolCall.ToolCallId, toolCall.ToolName, result);
            }
        }

        Console.WriteLine($"Stopped after {maxTurns} model calls without a final answer.");

        // Replace with your own tool dispatch. The arguments arrive as a JSON object.
        static string ExecuteTool(string toolName, string argumentsJson) => $"Executed {toolName} with {argumentsJson}.";
        // </AnthropicLoop>
    }
}
