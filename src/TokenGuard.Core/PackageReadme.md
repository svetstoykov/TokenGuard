# TokenGuard.Core

TokenGuard.Core keeps your agent loop conversation inside `ConversationContext`. That object is the source of truth for the session. Before each model call, TokenGuard reads that history, builds a provider-ready snapshot, and compacts only that snapshot when needed.

```csharp
// conversationContext is source of truth for this loop.
// System prompt lives there with every other message.
conversationContext.SetSystemPrompt("You are a careful coding assistant.");

// Add user turn to same stored conversation history.
conversationContext.AddUserMessage("Fix this, make no mistake.");

// Build next provider request from that history.
// TokenGuard may compact this snapshot to fit budget.
// Stored history inside conversationContext does not change.
var prepared = await conversationContext.PrepareAsync(cancellationToken);

// Send only prepared snapshot to provider.
var input = prepared.Messages.ForOpenAI();
ChatCompletion response = await chatClient.CompleteChatAsync(input, cancellationToken: cancellationToken);
```

You keep appending system, user, assistant, and tool messages to `ConversationContext`. Everything happens inside that object. `PrepareAsync()` returns a `PrepareResult` describing what should go to the model right now.

## What it does

- tracks token growth across the full turn sequence
- masks stale tool results using a sliding-window strategy when the conversation crosses a configurable soft threshold
- summarizes old history with your LLM when masking alone is not enough
- falls back to emergency truncation as a last resort
- pins durable context that survives all compaction stages
- stays provider-agnostic in core, with adapter helpers for OpenAI and Anthropic
- integrates in minutes via `AddConversationContext(...)` and a standard DI factory

## Install

```bash
dotnet add package TokenGuard.Core
dotnet add package TokenGuard.Extensions.OpenAI
```

`TokenGuard.Core` is provider-neutral. The quick start below sends requests through OpenAI, so it also needs the `TokenGuard.Extensions.OpenAI` adapter.

## Quick start

The program below is a complete agent loop against OpenAI with one tool. In a new console project, add the two packages above and `Microsoft.Extensions.DependencyInjection`, replace the contents of `Program.cs` with the program, set the `OPENAI_API_KEY` environment variable, and run `dotnet run`.

```csharp
using Microsoft.Extensions.DependencyInjection;
using OpenAI.Chat;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Extensions;
using TokenGuard.Extensions.OpenAI;

var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
    ?? throw new InvalidOperationException("Set the OPENAI_API_KEY environment variable.");
var chatClient = new ChatClient("gpt-5.4-nano", apiKey);
var chatOptions = new ChatCompletionOptions
{
    Tools = { ChatTool.CreateFunctionTool("list_failing_tests", "Lists the tests that fail in the repository.") },
};

var services = new ServiceCollection();
services.AddConversationContext(builder => builder
    .WithMaxTokens(40_000)
    .WithCompactionThreshold(0.80));

using var serviceProvider = services.BuildServiceProvider();
using var conversationContext = serviceProvider
    .GetRequiredService<IConversationContextFactory>()
    .Create();

conversationContext.SetSystemPrompt("You are a precise coding assistant.");
conversationContext.AddPinnedMessage(MessageRole.User, "Repository root is /workspace/project.");
conversationContext.AddUserMessage("Summarize the failing tests.");

while (true)
{
    var prepared = await conversationContext.PrepareAsync();

    if (prepared.Outcome == PrepareOutcome.CannotCompact)
        throw new InvalidOperationException(prepared.BudgetFailureReason);

    ChatCompletion response = await chatClient.CompleteChatAsync(prepared.Messages.ForOpenAI(), chatOptions);

    conversationContext.RecordModelResponse(response.ResponseSegments(), response.InputTokens());

    if (response.ToolCalls.Count == 0)
    {
        Console.WriteLine(response.Content[0].Text);
        break;
    }

    foreach (var toolCall in response.ToolCalls)
    {
        var result = ExecuteTool(toolCall);
        conversationContext.RecordToolResult(toolCall.Id, toolCall.FunctionName, result);
    }
}

static string ExecuteTool(ChatToolCall toolCall) => toolCall.FunctionName switch
{
    "list_failing_tests" => "OrderTests.Total_IncludesTax failed: expected 108.00 but was 100.00.",
    _ => $"Unknown tool {toolCall.FunctionName}.",
};
```

`PrepareAsync()` returns a `PrepareResult`, not just a message list. `PrepareResult.Messages` is the prepared snapshot to send to the provider. `ConversationContext.History` remains unchanged.

`PrepareAsync()` returns what was recorded, so a context with no messages gives an empty `Ready` result: record at least one user message before the first model call.

### Register at startup

The program registers a 40,000-token window with `AddConversationContext(...)`, so compaction starts at 32,000 tokens.

Default built-in pipeline starts compaction at **80%**, always runs sliding-window masking first, and keeps LLM summarization off until you register it explicitly.

Emergency truncation is **on by default at 1.0**. It fires only at the absolute token limit and acts as a last-resort safety net after the normal compaction pipeline has already run.

Override with `WithEmergencyThreshold(0.95)` to trigger earlier, or call `WithoutEmergencyThreshold()` to disable it entirely.

### Create a context per conversation

Configuration is singleton-scoped. Each `Create()` call returns an independent stateful context, so separate contexts can be used concurrently. A single context is for one caller at a time and nothing enforces that: if you record tool results from parallel tool calls on one context, serialize the calls yourself.

## Observability

Contexts created by an `IConversationContextFactory` resolved from a container that has a logging provider registered log through the container's `ILoggerFactory`. Without DI, pass
`WithLoggerFactory(loggerFactory)` to the builder. TokenGuard also emits activities and metrics under the names in
`TokenGuardDiagnostics` (`ActivitySourceName` and `MeterName`, both `TokenGuard`). None of them contain conversation
content. See [Observability](https://github.com/svetstoykov/TokenGuard/blob/main/docs/observability.md).

## More detail

- [Root README](https://github.com/svetstoykov/TokenGuard/blob/main/README.md)
- [How TokenGuard Thinks About Context](https://github.com/svetstoykov/TokenGuard/blob/main/docs/deep-dive/context-management.md)
