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
var response = await chatClient.CompleteChatAsync(input, cancellationToken: cancellationToken);
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
```

## Quick start

### 1. Register at startup

```csharp
services.AddConversationContext(builder => builder
    .WithMaxTokens(25_000)
    .WithCompactionThreshold(0.80));
```

Default built-in pipeline starts compaction at **80%**, always runs sliding-window masking first, and keeps LLM summarization off until you register it explicitly.

Emergency truncation is **on by default at 1.0**. It fires only at the absolute token limit and acts as a last-resort safety net after the normal compaction pipeline has already run.

Override with `WithEmergencyThreshold(0.95)` to trigger earlier, or call `WithoutEmergencyThreshold()` to disable it entirely.

### 2. Create a context per conversation

```csharp
using var conversationContext = serviceProvider
    .GetRequiredService<IConversationContextFactory>()
    .Create();
```

Configuration is singleton-scoped. Each `Create()` call returns an independent stateful context, safe to use across concurrent requests.

### 3. Run the loop

```csharp
using TokenGuard.Core.Enums;
using TokenGuard.Extensions.OpenAI;

var factory = serviceProvider.GetRequiredService<IConversationContextFactory>();

using var conversationContext = factory.Create();

conversationContext.SetSystemPrompt("You are a precise coding assistant.");
conversationContext.AddPinnedMessage(MessageRole.User, "Repository root is /workspace/project.");
conversationContext.AddUserMessage("Summarize the failing tests.");

while (true)
{
    var prepared = await conversationContext.PrepareAsync(cancellationToken);

    if (prepared.Outcome == PrepareOutcome.CannotCompact)
        throw new InvalidOperationException(prepared.BudgetFailureReason);

    var response = await chatClient.CompleteChatAsync(
        prepared.Messages.ForOpenAI(),
        chatOptions,
        cancellationToken);

    conversationContext.RecordModelResponse(
        response.ResponseSegments(),
        response.InputTokens());

    if (response.ToolCalls.Count == 0)
        break;

    foreach (var toolCall in response.ToolCalls)
    {
        var result = toolExecutor.Execute(toolCall);
        conversationContext.RecordToolResult(toolCall.Id, toolCall.FunctionName, result);
    }
}
```

`PrepareAsync()` returns a `PrepareResult`, not just a message list. `PrepareResult.Messages` is the prepared snapshot to send to the provider. `ConversationContext.History` remains unchanged.

## More detail

- [Root README](https://github.com/svetstoykov/TokenGuard/blob/main/README.md)
- [How TokenGuard Thinks About Context](https://github.com/svetstoykov/TokenGuard/blob/main/docs/deep-dive/context-management.md)
