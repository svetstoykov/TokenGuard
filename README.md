<div align="center">

# TokenGuard

**Token budget management for LLM agent loops.**

[![NuGet](https://img.shields.io/nuget/v/TokenGuard.Core?style=flat-square&color=5c2d91)](https://nuget.org/packages/TokenGuard.Core)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square)](https://dotnet.microsoft.com)
[![License: MIT](https://img.shields.io/badge/License-MIT-green?style=flat-square)](LICENSE)

</div>

---

TokenGuard wraps your message list and keeps each prepared payload under a configured token budget. It masks stale tool
output when pressure builds, drops the oldest unpinned messages when masking isn't enough, and leaves your raw history
intact. Integration is one call before each provider request.

```csharp
var preparedMessages = await conversationContext.PrepareAsync(cancellationToken);
```

---

## What it does

- **Tracks token growth** across the full turn sequence — user, assistant, tool, system, and pinned messages
- **Masks stale tool results** using a sliding-window strategy when the conversation crosses a configurable soft
  threshold
- **Falls back to emergency truncation** when masking alone cannot recover enough budget — drops oldest eligible
  messages, preserves everything pinned
- **Pins durable context** that survives both compaction tiers: system prompts, task constraints, repository rules, any
  message you need to live forever
- **Stays provider-agnostic** in core, with first-class adapter helpers for OpenAI and Anthropic
- **Emits compaction events** through `ICompactionObserver` for observability, logging, and diagnostics
- **Integrates in minutes** via `AddConversationContext(...)` and a standard DI factory

---

## Benchmark

A 22-turn tool-heavy session from [`samples/Codexplorer`](samples/Codexplorer), a simple coding agent, run under a
20,000-token budget.

> **~160,000 tokens saved. 39% cost reduction.**

|                         | Without TokenGuard |                With TokenGuard |
|-------------------------|-------------------:|-------------------------------:|
| Cumulative input tokens |            407,560 |                    **247,357** |
| Peak context size       |      34,394 tokens |              **19,124 tokens** |
| Billing reduction       |                    | **39.3% (~160K tokens saved)** |

Three compaction events kept the session alive and affordable:

| Turn | Before compaction | After compaction | Reduction |
|-----:|------------------:|-----------------:|----------:|
|    6 |     16,736 tokens |    16,209 tokens |      3.1% |
|    9 |     17,926 tokens |     8,260 tokens | **53.9%** |
|   18 |     32,822 tokens |    19,124 tokens | **41.7%** |

Without TokenGuard, the session would have crashed the context budget from turn 11 onward.  
Full numbers in [`samples/Codexplorer/README.md`](samples/Codexplorer/README.md).

<details>
<summary>Benchmark configuration</summary>

|                     |                                                                      |
|---------------------|----------------------------------------------------------------------|
| Sample              | [`samples/Codexplorer`](samples/Codexplorer) — a simple coding agent |
| Turns               | 22 (tool-heavy)                                                      |
| Model               | `openai/gpt-5.4-nano`                                                |
| Context budget      | 20,000 tokens                                                        |
| Soft threshold      | 0.80 → compaction triggers at 16,000                                 |
| Emergency threshold | 1.0 → hard cap at 20,000                                             |

</details>

---

## Install

```bash
dotnet add package TokenGuard.Core
dotnet add package TokenGuard.Extensions.OpenAI      # or Anthropic
```

---

## Quick start

### 1. Register at startup

```csharp
services.AddConversationContext(builder => builder
    .WithMaxTokens(100_000)
    .WithCompactionThreshold(0.80)
    .WithEmergencyThreshold(0.95));
```

Multiple named profiles work too:

```csharp
services.AddConversationContext("analysis", builder => builder
    .WithMaxTokens(200_000)
    .WithCompactionThreshold(0.75)
    .WithEmergencyThreshold(0.90));
```

`WithEmergencyThreshold` is optional. Skip it and there's no fallback once masking saturates — fine for short sessions,
risky for anything long-running.

Sliding-window masking is always active. Add provider-backed summarization only through the provider extension packages:

```csharp
services.AddConversationContext(builder => builder
    .WithMaxTokens(100_000)
    .WithSlidingWindowOptions(new SlidingWindowOptions(windowSize: 12))
    .UseLlmSummarization(chatClient));
```

```csharp
services.AddConversationContext(builder => builder
    .WithMaxTokens(100_000)
    .UseLlmSummarization(anthropicClient, "claude-3-7-sonnet-latest"));
```

### 2. Create a context per conversation

```csharp
using var conversationContext = serviceProvider
    .GetRequiredService<IConversationContextFactory>()
    .Create();
```

Configuration is singleton-scoped. Each `Create()` call returns an independent stateful context, safe to use across
concurrent requests.

### 3. Run the loop

```csharp
using TokenGuard.Extensions.OpenAI;

var factory = serviceProvider.GetRequiredService<IConversationContextFactory>();
using var conversationContext = factory.Create("coding-assistant");

conversationContext.SetSystemPrompt("You are a precise coding assistant.");
conversationContext.AddPinnedMessage(MessageRole.User, "Repository root is /workspace/project.");
conversationContext.AddUserMessage("Summarize the failing tests.");

while (true)
{
    var preparedMessages = await conversationContext.PrepareAsync(cancellationToken);
    var response = await chatClient.CompleteChatAsync(
        preparedMessages.ForOpenAI(), chatOptions, cancellationToken);

    conversationContext.RecordModelResponse(
        response.ResponseSegments(),
        response.InputTokens());

    if (response.ToolCalls.Count == 0)
        break;

    foreach (var toolCall in response.ToolCalls)
    {
        var result = toolExecutor.Execute(toolCall);
        conversationContext.RecordToolResult(toolCall.Id, toolCall.FunctionName, result);
    }
}
```

`PrepareAsync()` returns a snapshot. It does not mutate `History`, so your raw history stays intact.

---

## Pinned messages

Some context needs to survive the whole session — task constraints, repository layout, coding standards.

```csharp
conversationContext.SetSystemPrompt("You are a senior Go engineer.");
conversationContext.AddPinnedMessage(MessageRole.User, "All file paths must be relative to /workspace.");
```

Pinned messages are never masked, never dropped, and reinserted at their original positions after each compaction pass.
They count against the budget so their cost is always accounted for.

---

## How compaction works

Want architecture detail and trade-offs? Read [How TokenGuard Thinks About Context](docs/deep-dive/context-management.md).

Two ordered tiers:

**1. Observation masking.** The sliding-window strategy walks backwards through history and masks tool results outside
the active window. Recent turns stay intact, structure is preserved, message count doesn't change. This runs first
whenever the soft threshold is crossed.

**2. Emergency truncation** *(optional)*. If the masked payload still exceeds the emergency threshold, TokenGuard drops
the oldest unpinned messages until it fits.

---

## Provider adapters

The core has no provider dependency. Adapters handle the conversion in both directions.

**OpenAI**

```csharp
var messages = preparedMessages.ForOpenAI();
conversationContext.RecordModelResponse(response.ResponseSegments(), response.InputTokens());
```

Optional LLM summarization addon:

```csharp
builder.UseLlmSummarization(chatClient);
```

**Anthropic**

```csharp
var messages = preparedMessages.ForAnthropic();
conversationContext.RecordModelResponse(response.ResponseSegments(), response.InputTokens());
```

Optional LLM summarization addon:

```csharp
builder.UseLlmSummarization(anthropicClient, "claude-3-7-sonnet-latest");
```

---

## Observability

```csharp
services.AddConversationContext(builder => builder
    .WithMaxTokens(100_000)
    .WithCompactionThreshold(0.80)
    .WithObserver(new MyCompactionObserver()));
```

`ICompactionObserver` fires on each compaction event with message counts and before/after token totals. Wire it to your
logger, metrics pipeline, or dashboard.

---

## Without DI

If you're not using a container, construct directly:

```csharp
var budget = new ContextBudget(maxTokens: 100_000, compactionThreshold: 0.80);
var context = new ConversationContext(budget, tokenCounter, compactionStrategy);
```

DI is the recommended path. Direct construction is there for tests and constrained environments.

---

## Repository layout

```
src/
  TokenGuard.Core                     core abstractions, message model, compaction pipeline
  TokenGuard.Extensions.OpenAI        OpenAI message conversion and response mapping
  TokenGuard.Extensions.Anthropic     Anthropic message conversion and response mapping

samples/
  Codexplorer                         repository-analysis sample, benchmark reference

tests/
  TokenGuard.Tests                    unit tests
  TokenGuard.IntegrationTests         cross-component coverage

docs/                                supporting notes and documentation
ai/skills/                           shared agent workflow guidance
```

---

## Build and test

```bash
dotnet build TokenGuard.sln
dotnet test TokenGuard.sln --no-restore
```

Codexplorer is not part of `TokenGuard.sln`. Build it from its own directory:

```bash
cd samples/Codexplorer
dotnet build ./src/Codexplorer.csproj
```

---

## Requirements

- .NET SDK 10.0+
- LLM provider API key for live samples
- macOS, Linux, or Windows

---

## Current Status 🚧

What is current:

- sliding-window observation masking is implemented and usable now
- masking is implemented for normal pressure, and optional emergency truncation is available when an emergency threshold
  is configured
- pinned messages are implemented and survive both compaction tiers
- DI registration via `AddConversationContext(...)` and factory-based creation is implemented
- OpenAI and Anthropic adapter helpers are available
- runtime recording flow is available through `SetSystemPrompt(...)`, `AddPinnedMessage(...)`, `AddUserMessage(...)`,
  `PrepareAsync(...)`, `RecordModelResponse(...)`, and `RecordToolResult(...)`
- compaction observer callbacks are available through `ICompactionObserver`

What remains planned:

- summarization-based compaction
- broader multi-strategy pipeline expansion beyond current masking + emergency fallback
