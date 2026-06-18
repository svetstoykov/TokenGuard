# TokenGuard.Extensions.OpenAI

OpenAI adapter package for TokenGuard on .NET 10. It converts prepared TokenGuard messages to OpenAI chat messages and lets TokenGuard use OpenAI to summarize older history when context gets tight.

```csharp
services.AddConversationContext(builder => builder
    .WithMaxTokens(25_000)
    .UseLlmSummarization(chatClient));

var prepared = await conversation.PrepareAsync(cancellationToken);
var messages = prepared.Messages.ForOpenAI();

var response = await chatClient.CompleteChatAsync(messages, cancellationToken: cancellationToken);
conversation.RecordModelResponse(response.ResponseSegments(), response.InputTokens());
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

### 1. Add the provider integration

```csharp
using TokenGuard.Extensions.OpenAI;

services.AddConversationContext(builder => builder
    .WithMaxTokens(25_000)
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
var response = await chatClient.CompleteChatAsync(
    messages,
    chatOptions,
    cancellationToken);

conversation.RecordModelResponse(
    response.ResponseSegments(),
    response.InputTokens());
```

`ForOpenAI()` validates tool-call and tool-result structure. If the prepared history would create an orphaned tool result or a mismatched assistant/tool sequence, it throws before the request goes out.

## More detail

- [Root README](https://github.com/svetstoykov/TokenGuard/blob/main/README.md)
- [How TokenGuard Thinks About Context](https://github.com/svetstoykov/TokenGuard/blob/main/docs/deep-dive/context-management.md)
