# TokenGuard.Extensions.Anthropic

Anthropic adapter package for TokenGuard on .NET 10. It converts prepared TokenGuard messages to Anthropic request parts and lets TokenGuard use Anthropic to summarize older history when context gets tight.

```csharp
services.AddConversationContext(builder => builder
    .WithMaxTokens(25_000)
    .UseLlmSummarization(anthropicClient, "claude-3-7-sonnet-latest"));

var prepared = await conversation.PrepareAsync(cancellationToken);
var (messages, systemPrompt) = prepared.Messages.ForAnthropic();
```

Use this package when you want the core conversation model to stay provider-agnostic and only adapt to Anthropic at the boundary.

## What it does

- adds Anthropic-backed summarization through `UseLlmSummarization(...)`
- converts prepared TokenGuard messages with `ForAnthropic()`
- returns system content separately, which matches the Anthropic API shape
- keeps the Anthropic-specific behavior out of `TokenGuard.Core`

## Install

```bash
dotnet add package TokenGuard.Core
dotnet add package TokenGuard.Extensions.Anthropic
```

## Quick start

### 1. Add the provider integration

```csharp
using TokenGuard.Extensions.Anthropic;

services.AddConversationContext(builder => builder
    .WithMaxTokens(25_000)
    .UseLlmSummarization(anthropicClient, "claude-3-7-sonnet-latest"));
```

### 2. Prepare the request

```csharp
var prepared = await conversation.PrepareAsync(cancellationToken);
var (messages, systemPrompt) = prepared.Messages.ForAnthropic();
```

### 3. Send the Anthropic request

Use `messages` as the Anthropic message list and `systemPrompt` as the separate system value in your request builder.

`ForAnthropic()` keeps the shape aligned with Anthropic's API, where system content is separate from the main message array.

## More detail

- [Root README](https://github.com/svetstoykov/TokenGuard/blob/main/README.md)
- [How TokenGuard Thinks About Context](https://github.com/svetstoykov/TokenGuard/blob/main/docs/deep-dive/context-management.md)
