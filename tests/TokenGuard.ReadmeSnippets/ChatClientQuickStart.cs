// <ChatClientUsings>
using Microsoft.Extensions.AI;
using TokenGuard.Core;
using TokenGuard.Core.Configuration;
using TokenGuard.Core.Enums;
using TokenGuard.Extensions.AI;
// </ChatClientUsings>

namespace TokenGuard.ReadmeSnippets;

/// <summary>Holds the chat-client quick start compiled against packed packages.</summary>
internal static class ChatClientQuickStart
{
    // <ChatClientLoop>
    // Supply your provider's IChatClient and any tool declarations in options.
    // Keep this client alive for the lifetime of its contexts.
    static async Task RunAsync(IChatClient chatClient, ChatOptions options, CancellationToken cancellationToken = default)
    {
        var configuration = new ConversationConfigBuilder()
            .WithMaxTokens(40_000)
            .UseLlmSummarization(chatClient)
            .Build();
        using var context = new ConversationContextFactory(configuration).Create();
        context.SetSystemPrompt("You are a precise coding assistant.");
        context.AddPinnedMessage(MessageRole.User, "Repository root is /workspace/project.");
        context.AddUserMessage("Summarize the failing tests.");

        for (var turn = 0; turn < 20; turn++)
        {
            var prepared = await context.PrepareAsync(cancellationToken);
            if (prepared.Outcome is PrepareOutcome.CompactionInsufficient or PrepareOutcome.CannotCompact)
                throw new InvalidOperationException(prepared.BudgetFailureReason);

            var response = await chatClient.GetResponseAsync(prepared.Messages.ForChatClient(), options, cancellationToken);
            var segments = response.ResponseSegments();
            if (segments.Count == 0)
                throw new InvalidOperationException("The model returned an empty reply.");

            context.RecordModelResponse(segments, response.InputTokens());
            var toolCalls = response.ToolUseSegments();
            if (toolCalls.Count == 0)
            {
                Console.WriteLine(string.Join(Environment.NewLine, response.TextSegments().Select(segment => segment.Content)));
                return;
            }

            foreach (var toolCall in toolCalls)
            {
                var result = ExecuteTool(toolCall.ToolName, toolCall.Content);
                context.RecordToolResult(toolCall.ToolCallId, toolCall.ToolName, result);
            }
        }

        throw new InvalidOperationException("Stopped after 20 model calls without a final answer.");

        // Replace with your own dispatch; argumentsJson holds the tool's JSON object.
        static string ExecuteTool(string name, string argumentsJson) => $"Executed {name} with {argumentsJson}.";
    }
    // </ChatClientLoop>
}
