# TokenGuard.Extensions.OpenAI

OpenAI adapter for TokenGuard. It converts prepared TokenGuard messages into OpenAI chat messages and can use OpenAI for summarization when context gets tight.

## Install

```bash
dotnet add package TokenGuard.Core
dotnet add package TokenGuard.Extensions.OpenAI
```

## What it does

- adds OpenAI-backed summarization through `UseLlmSummarization(...)`
- converts prepared messages with `ForOpenAI()`
- validates tool-call and tool-result pairing before the request is sent

## Basic use

```csharp
services.AddConversationContext(builder => builder
    .WithMaxTokens(25_000)
    .UseLlmSummarization(chatClient));

var prepared = await conversation.PrepareAsync(cancellationToken);
var messages = prepared.Messages.ForOpenAI();

var response = await chatClient.CompleteChatAsync(messages, cancellationToken: cancellationToken);
conversation.RecordModelResponse(response.ResponseSegments(), response.InputTokens());
```

## More detail

- [Root README](https://github.com/svetstoykov/TokenGuard/blob/main/README.md)
- [How TokenGuard Thinks About Context](https://github.com/svetstoykov/TokenGuard/blob/main/docs/deep-dive/context-management.md)
