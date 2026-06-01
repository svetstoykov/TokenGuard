# TokenGuard.Core

Core TokenGuard package for .NET 10 agent loops. Tracks conversation growth, prepares provider-ready snapshots, and compacts older history when token pressure builds.

## Install

```bash
dotnet add package TokenGuard.Core
```

## Use

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

Add `TokenGuard.Extensions.OpenAI` or `TokenGuard.Extensions.Anthropic` when you need provider adapters or LLM-backed summarization.
