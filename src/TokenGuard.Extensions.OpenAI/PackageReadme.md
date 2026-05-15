# TokenGuard.Extensions.OpenAI

OpenAI adapter package for TokenGuard on .NET 10. Adds OpenAI-backed summarization and converts prepared TokenGuard messages to OpenAI chat messages.

## Install

```bash
dotnet add package TokenGuard.Core
dotnet add package TokenGuard.Extensions.OpenAI
```

## Use

```csharp
services.AddConversationContext(builder => builder
    .WithMaxTokens(25_000)
    .UseLlmSummarization(chatClient));

var prepared = await conversation.PrepareAsync(cancellationToken);
var messages = prepared.Messages.ForOpenAI();

var response = await chatClient.CompleteChatAsync(messages, cancellationToken: cancellationToken);
conversation.RecordModelResponse(response.ResponseSegments(), response.InputTokens());
```
