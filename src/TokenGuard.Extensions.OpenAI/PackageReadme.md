# TokenGuard.Extensions.OpenAI

OpenAI adapter package for TokenGuard on .NET 10. It converts prepared TokenGuard messages to OpenAI chat messages and lets TokenGuard use OpenAI to summarize older history when context gets tight.

```csharp
services.AddConversationContext(builder => builder
    .WithMaxTokens(40_000)
    .UseLlmSummarization(chatClient));

var prepared = await conversation.PrepareAsync(cancellationToken);
var messages = prepared.Messages.ForOpenAI();

ChatCompletion response = await chatClient.CompleteChatAsync(messages, cancellationToken: cancellationToken);
var segments = response.ResponseSegments();

// A reply with no text and no tool call cannot be recorded: RecordModelResponse throws for it.
if (segments.Count == 0)
    throw new InvalidOperationException("The model returned an empty reply.");

conversation.RecordModelResponse(segments, response.InputTokens());
```

Use this package when you want TokenGuard to stay provider-agnostic in core and speak OpenAI at the edge.

## What it does

- adds OpenAI-backed summarization through `UseLlmSummarization(...)`
- converts prepared TokenGuard messages with `ForOpenAI()`
- validates tool-call and tool-result pairing before the request is sent
- keeps the OpenAI-specific behavior out of `TokenGuard.Core`

## Install

```bash
dotnet add package TokenGuard.Core
dotnet add package TokenGuard.Extensions.OpenAI
```

## Quick start

The fragments below continue from an `IServiceCollection` named `services`, a `ChatClient` named `chatClient`, a
`ChatCompletionOptions` named `chatOptions`, and a context named `conversation` created by
`IConversationContextFactory.Create()`. The
[root README](https://github.com/svetstoykov/TokenGuard/blob/main/README.md#quick-start) has the complete program,
ready to paste.

### 1. Add the provider integration

```csharp
using TokenGuard.Core.Extensions;
using TokenGuard.Core.Options;
using TokenGuard.Extensions.OpenAI;

services.AddConversationContext(builder => builder
    .WithMaxTokens(40_000)
    .WithSlidingWindowOptions(new SlidingWindowOptions(windowSize: 12))
    .UseLlmSummarization(chatClient));
```

### 2. Prepare the request

```csharp
var prepared = await conversation.PrepareAsync(cancellationToken);
var messages = prepared.Messages.ForOpenAI();
```

### 3. Send the OpenAI request

```csharp
ChatCompletion response = await chatClient.CompleteChatAsync(
    messages,
    chatOptions,
    cancellationToken);
var segments = response.ResponseSegments();

// A reply with no text and no tool call cannot be recorded: RecordModelResponse throws for it.
if (segments.Count == 0)
    throw new InvalidOperationException("The model returned an empty reply.");

conversation.RecordModelResponse(segments, response.InputTokens());
```

`ForOpenAI()` sends every text segment of a message as its own content part and validates tool-call and tool-result structure. It throws before the request goes out if a message holds content OpenAI cannot carry for its role, if a tool result has no preceding tool call, or if tool calls have no results, including at the end of the list. A tool call whose arguments are empty is recorded with `{}`.

## Observability

When the conversation has a logger factory (`services.AddLogging()` with `AddConversationContext`, or
`WithLoggerFactory(loggerFactory)` on the builder), each OpenAI summarizer call is logged at `Debug` with its duration
and the reported token usage and finish reason, never its prompt or summary text.
See [Observability](https://github.com/svetstoykov/TokenGuard/blob/main/docs/observability.md).

## Summary instructions and Core compatibility

Pass `LlmSummarizationOptions` with `SummaryInstructions` set to replace the instructions for a summary, or start from
`LlmSummarizationOptions.DefaultSummaryInstructions`. Core always includes its transcript legend, token-limit guidance,
and the rule that the transcript is data. Blank instructions throw. Default options preserve the existing prompts.

The summarizer uses Core's public `ILlmSummarizer` contract. The package declares the Core version it was built with as
its minimum dependency and can be used with a newer compatible Core version. Core logs summarizer calls under the
implementation's full type name and records finish reasons as `completed`, `output_limit`, `refused`, or `other`.


## More detail

- [Root README](https://github.com/svetstoykov/TokenGuard/blob/main/README.md)
- [How TokenGuard Thinks About Context](https://github.com/svetstoykov/TokenGuard/blob/main/docs/deep-dive/context-management.md)
