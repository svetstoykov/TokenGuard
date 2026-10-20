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
- masks stale tool results using a sliding-window strategy when the conversation crosses the compaction threshold
- summarizes old history with your LLM when masking alone is not enough
- falls back to emergency truncation as a last resort
- pins durable context that survives all compaction stages
- stays provider-agnostic in core, with adapter helpers for OpenAI and Anthropic
- integrates in minutes via `AddConversationContext(...)` and a standard DI factory

## Install

```bash
dotnet add package TokenGuard.Core
dotnet add package TokenGuard.Extensions.OpenAI
dotnet add package Microsoft.Extensions.DependencyInjection
```

`TokenGuard.Core` is provider-neutral. The quick start below sends requests through OpenAI, so it also needs the `TokenGuard.Extensions.OpenAI` adapter, and it registers TokenGuard in the `ServiceCollection` that `Microsoft.Extensions.DependencyInjection` supplies. For Anthropic, add `TokenGuard.Extensions.Anthropic` in place of the OpenAI adapter; its package page has the same program for Anthropic.

## Quick start

The program below is a complete agent loop against OpenAI with one tool. In a new console project, add the three packages above, replace the contents of `Program.cs` with the program, set the `OPENAI_API_KEY` environment variable, and run `dotnet run`.

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

const int maxTurns = 20;

for (var turn = 1; turn <= maxTurns; turn++)
{
    var prepared = await conversationContext.PrepareAsync();

    // Both outcomes mean the prepared request is still over the budget, so stop before the provider rejects it.
    if (prepared.Outcome is PrepareOutcome.CompactionInsufficient or PrepareOutcome.CannotCompact)
        throw new InvalidOperationException(prepared.BudgetFailureReason);

    ChatCompletion response = await chatClient.CompleteChatAsync(prepared.Messages.ForOpenAI(), chatOptions);
    var segments = response.ResponseSegments();

    // A reply with no text and no tool call cannot be recorded: RecordModelResponse throws for it.
    if (segments.Count == 0)
        throw new InvalidOperationException("The model returned an empty reply.");

    conversationContext.RecordModelResponse(segments, response.InputTokens());

    if (response.ToolCalls.Count == 0)
    {
        Console.WriteLine(string.Join(Environment.NewLine, response.TextSegments().Select(segment => segment.Content)));
        return;
    }

    foreach (var toolCall in response.ToolCalls)
    {
        var result = ExecuteTool(toolCall);
        conversationContext.RecordToolResult(toolCall.Id, toolCall.FunctionName, result);
    }
}

Console.WriteLine($"Stopped after {maxTurns} model calls without a final answer.");

static string ExecuteTool(ChatToolCall toolCall) => toolCall.FunctionName switch
{
    "list_failing_tests" => "OrderTests.Total_IncludesTax failed: expected 108.00 but was 100.00.",
    _ => $"Unknown tool {toolCall.FunctionName}.",
};
```

`PrepareAsync()` returns a `PrepareResult`, not just a message list. `PrepareResult.Messages` is the prepared snapshot to send to the provider. `ConversationContext.History` remains unchanged.

`PrepareAsync()` returns what was recorded, so a context with no messages gives an empty `Ready` result: record at least one user message before the first model call.

The loop returns when the model answers without a tool call, throws when the outcome is `CompactionInsufficient` or `CannotCompact`, throws when the model returns an empty reply, and ends after `maxTurns` model calls. An empty reply is not recorded: `RecordModelResponse` throws `ArgumentException` for a reply with no segments, so check the segments first.

### Register at startup

The program registers a 40,000-token budget with `AddConversationContext(...)`, so compaction starts at 32,000 tokens.

Default built-in pipeline starts compaction at **80%**, always runs sliding-window masking first, and keeps LLM summarization off until you register it explicitly.

Emergency truncation is **on by default at 1.0**. It fires only at the absolute token limit and acts as a last-resort safety net after the normal compaction pipeline has already run.

Override with `WithEmergencyThreshold(0.95)` to trigger earlier, or call `WithoutEmergencyThreshold()` to disable it entirely.

### Create a context per conversation

Configuration is singleton-scoped. Each `Create()` call returns an independent stateful context, so separate contexts can be used concurrently. A single context is for one caller at a time: a call that starts while another call on the same context is still running throws `InvalidOperationException`, on a best-effort basis. Run parallel tool calls in parallel and record their results one after another.

## PrepareResult and outcomes

`PrepareResult.Outcome` tells you whether to send `PrepareResult.Messages`.

| Outcome | Meaning |
|---|---|
| `Ready` | The request fits and no message was changed |
| `Compacted` | The request fits after messages were masked, summarized, or dropped |
| `CompactionInsufficient` | Messages were masked, summarized, or dropped, and the request is still over the budget |
| `CannotCompact` | The request is over the budget and no message could be changed |

For the two over-budget outcomes `BudgetFailureReason` carries the diagnostic text. An overrun within the tolerance, 5% of the budget by default and set with `WithOverrunTolerance(...)`, counts as fitting. `TokensBeforeCompaction`, `TokensAfterCompaction`, `MessagesCompacted`, and `MessagesDropped` report what the call did. See [PrepareResult](https://github.com/svetstoykov/TokenGuard/blob/main/README.md#prepareresult) and [Compaction statuses](https://github.com/svetstoykov/TokenGuard/blob/main/README.md#compaction-statuses).

## Pinned messages

`SetSystemPrompt(...)` and `AddPinnedMessage(...)` record messages that are never masked, never summarized, and never dropped. They still count against the budget, and a pinned message cannot be removed once recorded. When pinned messages alone exceed the budget, `PrepareAsync()` throws `PinnedTokenBudgetExceededException`. See [Pinned messages](https://github.com/svetstoykov/TokenGuard/blob/main/README.md#pinned-messages).

## LLM summarization

Sliding-window masking is always on. Summarization is off until you register it with `UseLlmSummarization(...)` from `TokenGuard.Extensions.OpenAI` or `TokenGuard.Extensions.Anthropic`, or pass your own `ILlmSummarizer` to the builder. When masking leaves the history over the budget, TokenGuard then replaces the older messages with one summary message and keeps the newest messages verbatim: 5 by default, set with `LlmSummarizationOptions.WindowSize`. When a summarizer call fails, TokenGuard falls back to the masked history and reports the exception in `PrepareResult.SummarizationError`. See [LLM summarization](https://github.com/svetstoykov/TokenGuard/blob/main/README.md#llm-summarization).

Custom summarizers receive a `SummarizerRequest` with system and user prompts and an output cap and return a
`SummarizerResponse` with text, finish reason, and optional token usage. Core builds the transcript, validates the
response, and logs the call. The shared implementation must be safe for concurrent use. Set
`LlmSummarizationOptions.SummaryInstructions` to replace the instructions; `DefaultSummaryInstructions` exposes the
default text. Fixed transcript guidance is always included. See [Custom summarizer](https://github.com/svetstoykov/TokenGuard/blob/main/README.md#custom-summarizer).


## Token counting

TokenGuard estimates tokens with the heuristic `EstimatedTokenCounter`. The estimate leaves out request overhead such as tool schemas. Pass the provider-reported input count as the second argument of `RecordModelResponse`, as the quick start does with `response.InputTokens()`, and TokenGuard corrects its totals by the difference until the next report. See [Token counting](https://github.com/svetstoykov/TokenGuard/blob/main/README.md#token-counting).

## Observability

Contexts created by an `IConversationContextFactory` resolved from a container that has a logging provider registered log through the container's `ILoggerFactory`. Without DI, pass
`WithLoggerFactory(loggerFactory)` to the builder. TokenGuard also emits activities and metrics under the names in
`TokenGuardDiagnostics` (`ActivitySourceName` and `MeterName`, both `TokenGuard`). None of them contain conversation
content. See [Observability](https://github.com/svetstoykov/TokenGuard/blob/main/docs/observability.md).

## More detail

- [Root README](https://github.com/svetstoykov/TokenGuard/blob/main/README.md)
- [How TokenGuard Thinks About Context](https://github.com/svetstoykov/TokenGuard/blob/main/docs/deep-dive/context-management.md)
