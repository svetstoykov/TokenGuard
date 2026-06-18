# TokenGuard.Core

Core package for TokenGuard. It keeps long-running agent conversations inside `ConversationContext`, then prepares a smaller provider-ready snapshot when token pressure builds.

## Install

```bash
dotnet add package TokenGuard.Core
```

## What it does

- keeps full conversation history in `ConversationContext`
- prepares the next provider payload without rewriting stored history
- masks stale tool output before older turns are summarized
- falls back to emergency truncation only when the request still cannot fit
- keeps pinned messages and system prompts in place

## Basic use

```csharp
services.AddConversationContext(builder => builder
    .WithMaxTokens(25_000)
    .WithCompactionThreshold(0.80));

using var conversation = serviceProvider
    .GetRequiredService<IConversationContextFactory>()
    .Create();

conversation.SetSystemPrompt("You are a careful coding assistant.");
conversation.AddUserMessage("Summarize repo status.");

var prepared = await conversation.PrepareAsync(cancellationToken);
```

Send `prepared.Messages` to your provider. Use `TokenGuard.Extensions.OpenAI` or `TokenGuard.Extensions.Anthropic` when you need provider adapters or LLM-backed summarization.

## More detail

- [Root README](https://github.com/svetstoykov/TokenGuard/blob/main/README.md)
- [How TokenGuard Thinks About Context](https://github.com/svetstoykov/TokenGuard/blob/main/docs/deep-dive/context-management.md)
