# TokenGuard.Extensions.Anthropic

Anthropic adapter package for TokenGuard on .NET 10. It converts prepared TokenGuard messages to Anthropic request parts and lets TokenGuard use Anthropic to summarize older history when context gets tight.

```csharp
var prepared = await conversationContext.PrepareAsync(cancellationToken);
var (messages, systemPrompt) = prepared.Messages.ForAnthropic();
```

Use this package when you want the core conversation model to stay provider-agnostic and only adapt to Anthropic at the boundary.

## Status

This adapter is covered by unit tests only. No test in this repository sends a request to the Anthropic API, so the
points under [Not verified against the Anthropic API](#not-verified-against-the-anthropic-api) are open.

## What it does

- adds Anthropic-backed summarization through `UseLlmSummarization(...)`
- converts prepared TokenGuard messages with `ForAnthropic()`
- returns system content separately, which matches the Anthropic API shape
- maps a response back with `ResponseSegments()` and reports its total input tokens with `InputTokens()`
- keeps the Anthropic-specific behavior out of `TokenGuard.Core`

## Install

```bash
dotnet add package TokenGuard.Core
dotnet add package TokenGuard.Extensions.Anthropic
dotnet add package Microsoft.Extensions.DependencyInjection
```

## Quick start

The three parts below form one `Program.cs`. Set the `ANTHROPIC_API_KEY` environment variable before running it.

### 1. Usings

```csharp
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.DependencyInjection;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Extensions;
using TokenGuard.Extensions.Anthropic;
```

### 2. Register TokenGuard, create a context and define a tool

```csharp
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
```

`UseLlmSummarization(...)` is optional. Without it TokenGuard masks old tool results and does not call a summarizer.

### 3. Run the loop

```csharp
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

    conversationContext.RecordModelResponse(
        response.ResponseSegments(),
        response.InputTokens());

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
```

This quick start is compiled against the packed packages by `tests/TokenGuard.ReadmeSnippets/verify.sh` in release
validation. It has not been run against the Anthropic API.

## What `ForAnthropic()` sends

`ForAnthropic()` returns the message list and the system payload separately, because the Anthropic API carries system
content outside the message list.

- Every text segment that holds non-whitespace text becomes its own text block, in order.
- A model message becomes one assistant message: its text blocks, then one `tool_use` block per tool call.
- A tool message becomes one user message with one `tool_result` block per result.
- The text of every system message goes to the system payload, in order. The payload is `null` when there is no system
  message.

It sends everything it is given or throws `InvalidOperationException`. The message names the role and the problem. It
throws when:

- a user or system message has no text, or holds a segment that is not text
- a model message holds a segment that is neither text nor a tool call, or has neither text nor a tool call
- a tool message has no tool result, or holds a segment that is not a tool result
- the arguments of a tool call are not a JSON object
- a tool result has no preceding tool call, a tool call has no matching result before the next message that is not a
  tool message, or tool calls are unanswered at the end of the list

## Token counts

`response.InputTokens()` returns the total input of the request: the sum of `input_tokens`,
`cache_creation_input_tokens` and `cache_read_input_tokens`. With prompt caching, `input_tokens` alone counts only the
part of the request after the last cache breakpoint, so the sum is the value that matches what TokenGuard estimates.
It returns `null` when the response carries no usage; `RecordModelResponse` then stays on the heuristic estimate.

## Not verified against the Anthropic API

- Whether the API accepts a prepared view whose first message is the summary, which is an assistant message.
- Whether the API accepts one user message per tool result after a turn with several tool calls. Each
  `RecordToolResult` call records its own tool message, and `ForAnthropic()` sends one user message per tool message.
- How extended thinking behaves with tools. `ResponseSegments()` reads text and tool-use blocks only, so a thinking
  block is not recorded and is not sent back on the next request.

## Observability

When the conversation has a logger factory (`services.AddLogging()` with `AddConversationContext`, or
`WithLoggerFactory(loggerFactory)` on the builder), each Anthropic summarizer call is logged at `Debug` with its duration
and the reported token usage and finish reason, never its prompt or summary text.
See [Observability](https://github.com/svetstoykov/TokenGuard/blob/main/docs/observability.md).

## More detail

- [Root README](https://github.com/svetstoykov/TokenGuard/blob/main/README.md)
- [How TokenGuard Thinks About Context](https://github.com/svetstoykov/TokenGuard/blob/main/docs/deep-dive/context-management.md)
