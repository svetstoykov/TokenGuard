# TokenGuard.Extensions.AI

Token-budget management and summarization through any `IChatClient`.

```bash
dotnet add package TokenGuard.Extensions.AI
```

### Microsoft.Extensions.AI

Install `TokenGuard.Extensions.AI` and your provider's `IChatClient` implementation. The adapter depends on
`Microsoft.Extensions.AI.Abstractions` 10.11.0 or later and targets .NET 10. Keep one context per conversation and one
caller at a time. This helper accepts your client and tool declarations:

```csharp
using Microsoft.Extensions.AI;
using TokenGuard.Core;
using TokenGuard.Core.Configuration;
using TokenGuard.Core.Enums;
using TokenGuard.Extensions.AI;
```

```csharp
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
```

Drive each tool round in the caller, as above. `FunctionInvokingChatClient` returns intermediate function results and
aggregates usage across rounds; TokenGuard rejects responses containing `FunctionResultContent` with guidance to
remove automatic function invocation. This keeps the recorded history and provider correction aligned with each request.
See [Microsoft's implementation](https://github.com/dotnet/extensions/blob/v10.11.0/src/Libraries/Microsoft.Extensions.AI/ChatCompletion/FunctionInvokingChatClient.cs).

Leave `ChatOptions.ConversationId` unset and configure the provider client to send the complete prepared history;
server-managed history can retain content that TokenGuard compacted. For streaming, collect the updates with
`ToChatResponseAsync()` before using these response helpers. Text and function calls are supported; reasoning content
and usage content are omitted, and unsupported content such as media throws `NotSupportedException`. Function calls with
argument-mapping failures or marked informational throw `InvalidOperationException` rather than becoming executable tool requests.

TokenGuard keeps the history, correction and summary checkpoint in the caller's context. Its token budget uses provider
usage correction, masks old tool results in steps, and preserves pinned messages through every compaction stage.
The experimental Microsoft.Extensions.AI [chat reducers](https://github.com/dotnet/extensions/tree/v10.11.0/src/Libraries/Microsoft.Extensions.AI/ChatReduction)
reduce lists using message counts. [Agent Framework compaction](https://learn.microsoft.com/agent-framework/concepts/agents/conversations/compaction)
also supports token, turn and group triggers. This adapter supplies conversions and summarization for a caller-owned
TokenGuard context; it provides no middleware or `IChatReducer` registration.


The package uses Core's public summarizer contract and declares a minimum Core dependency.
The caller owns the client and keeps it safe for concurrent use across contexts.

See the [full documentation](https://github.com/svetstoykov/TokenGuard#readme).
