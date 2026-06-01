# TokenGuard.Extensions.Anthropic

Anthropic adapter package for TokenGuard on .NET 10. Adds Anthropic-backed summarization and converts prepared TokenGuard messages to Anthropic request payloads.

## Install

```bash
dotnet add package TokenGuard.Core
dotnet add package TokenGuard.Extensions.Anthropic
```

## Use

```csharp
services.AddConversationContext(builder => builder
    .WithMaxTokens(25_000)
    .UseLlmSummarization(anthropicClient, "claude-3-7-sonnet-latest"));

var prepared = await conversation.PrepareAsync(cancellationToken);
var (messages, systemPrompt) = prepared.Messages.ForAnthropic();
```
