# TokenGuard.Extensions.Anthropic

Anthropic adapter for TokenGuard. It converts prepared TokenGuard messages into Anthropic request payloads and can use Anthropic for summarization when context gets tight.

## Install

```bash
dotnet add package TokenGuard.Core
dotnet add package TokenGuard.Extensions.Anthropic
```

## What it does

- adds Anthropic-backed summarization through `UseLlmSummarization(...)`
- converts prepared messages with `ForAnthropic()`
- returns system content separately, which matches the Anthropic API shape

## Basic use

```csharp
services.AddConversationContext(builder => builder
    .WithMaxTokens(25_000)
    .UseLlmSummarization(anthropicClient, "claude-3-7-sonnet-latest"));

var prepared = await conversation.PrepareAsync(cancellationToken);
var (messages, systemPrompt) = prepared.Messages.ForAnthropic();
```

## More detail

- [Root README](https://github.com/svetstoykov/TokenGuard/blob/main/README.md)
- [How TokenGuard Thinks About Context](https://github.com/svetstoykov/TokenGuard/blob/main/docs/deep-dive/context-management.md)
