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
conversationContext.AddUserMessage("Fix this, make no mistake.");

// Applies compaction and prepares messages for the provider
var preparedMessages = await conversationContext.PrepareAsync(cancellationToken);

chatClient.CompleteChatAsync(preparedMessages.ForOpenAI(),
```

---

## What it does

- **Tracks token growth** across the full turn sequence — user, assistant, tool, system, and pinned messages
- **Masks stale tool results** using a sliding-window strategy when the conversation crosses a configurable soft
  threshold
- **Summarizes old history with your LLM** when masking alone isn't enough — collapses older turns into a compact
  summary message while keeping recent messages verbatim
- **Falls back to emergency truncation** as a last resort — drops oldest unpinned messages, preserves everything pinned
- **Pins durable context** that survives all compaction stages: system prompts, task constraints, repository rules, any
  message you need to live forever
- **Stays provider-agnostic** in core, with first-class adapter helpers for OpenAI and Anthropic
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
    .WithMaxTokens(25_000)
    .WithCompactionThreshold(0.80));
```

Emergency truncation is **on by default at 1.0** (fires only at the absolute token limit as a last-resort safety net).
When it fires, it **permanently drops the oldest unpinned messages** from the history until the context fits. This is
intentional: long-running sessions accumulate turns that are no longer relevant, and dropping them keeps the session
alive rather than crashing or stalling. Without this safety net, a context that masking and summarization could not
recover would terminate the session entirely.

Override with `WithEmergencyThreshold(0.95)` to trigger earlier, or call `WithoutEmergencyThreshold()` to disable it
entirely. Disabling is an option, but we advise against it for long-running sessions — the safety net is there precisely
for the cases where compaction alone is not enough.

Multiple named profiles work too:

```csharp
services.AddConversationContext("analysis", builder => builder
    .WithMaxTokens(200_000)
    .WithCompactionThreshold(0.75));
```

Sliding-window masking is always active. Add provider-backed summarization only through the provider extension packages:

```csharp
services.AddConversationContext(builder => builder
    .WithMaxTokens(25_000)
    .WithSlidingWindowOptions(new SlidingWindowOptions(windowSize: 12))
    .UseLlmSummarization(chatClient));
```

```csharp
services.AddConversationContext(builder => builder
    .WithMaxTokens(25_000)
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

// System messages are always added at the beginning.
conversationContext.SetSystemPrompt("You are a precise coding assistant.");

// Pinned messages can be added in the beginning as a sort of additional context.
conversationContext.AddPinnedMessage(MessageRole.User, "Repository root is /workspace/project.");
conversationContext.AddUserMessage("Summarize the failing tests.");

while (true)
{
    var preparedMessages = await conversationContext.PrepareAsync(cancellationToken);
    var response = await chatClient.CompleteChatAsync(preparedMessages.ForOpenAI(), chatOptions, cancellationToken);

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

Three ordered tiers:

**1. Observation masking.** The sliding-window strategy walks backwards through history and masks tool results outside
the active window. Recent turns stay intact, structure is preserved, message count doesn't change. This runs first
whenever the soft threshold is crossed.

**2. LLM summarization** *(opt-in — register with `UseLlmSummarization(...)`)*. If masking still leaves the context
over budget, TokenGuard calls your LLM to collapse older turns into a compact summary message. The newest messages stay
verbatim. This stage only runs if you registered a provider.

**3. Emergency truncation** *(on by default, opt-out with `WithoutEmergencyThreshold()`)*. If the context is still
over budget after all previous stages, TokenGuard drops the oldest unpinned messages until it fits.

---

## LLM summarization

When masking alone isn't enough, TokenGuard can use your LLM to replace older history with a single compact summary.
The newest messages stay verbatim — only older turns are collapsed. The summary is inserted as a regular message so the
model always has full context on what came before.

Register it with one extra call on your builder:

```csharp
// OpenAI — model is inferred from the ChatClient
builder.UseLlmSummarization(chatClient);

// Anthropic — model must be specified explicitly
builder.UseLlmSummarization(anthropicClient, "claude-3-7-sonnet-latest");
```

Defaults keep the last **5 messages** verbatim and bound the summary to **2 048–4 096 tokens**. Override with
`LlmSummarizationOptions`:

```csharp
builder.UseLlmSummarization(chatClient, new LlmSummarizationOptions(
    windowSize: 5,
    minSummaryTokens: 1024,
    maxSummaryTokens: 2048));
```

| Option | What it controls | Default |
|---|---|---|
| `WindowSize` | How many newest messages stay verbatim | 5 |
| `MinSummaryTokens` | Minimum budget required before summarizing (skips if budget is too small) | 2 048 |
| `MaxSummaryTokens` | Maximum budget forwarded to the summarizer | 4 096 |

Only one provider per builder. Registering both OpenAI and Anthropic on the same builder throws at startup.

---

## Provider adapters

The core has no provider dependency. Adapters handle the conversion in both directions.

**OpenAI**

```csharp

// In-going messages formatting
var messages = preparedMessages.ForOpenAI();

// Output from the mode that can be formatted for the ConversationContext
var formattedOutput = response.ResponseSegments();
conversationContext.RecordModelResponse(formattedOutput, response.InputTokens());
```

Optional LLM summarization addon:

```csharp
builder.UseLlmSummarization(chatClient);
```

**Anthropic**

```csharp
// In-going messages formatting
var messages = preparedMessages.ForAnthropic();

// Output from the mode that can be formatted for the ConversationContext
conversationContext.RecordModelResponse(response.ResponseSegments(), response.InputTokens());
```

Optional LLM summarization addon:

```csharp
builder.UseLlmSummarization(anthropicClient, "claude-3-7-sonnet-latest");
```

---

## Without DI

If you're not using a container, construct a factory directly:

```csharp
var factory = new ConversationContextFactory(
    new ConversationConfigBuilder()
        .WithMaxTokens(25_000)
        .WithCompactionThreshold(0.80)
        .Build());

using var context = factory.Create();
```

DI is the recommended path. Public factory is manual fallback when you don't want a container.

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

## Current Status

What is current:

- sliding-window observation masking is implemented and usable now
- masking is implemented for normal pressure, and emergency truncation is **on by default at 1.0** (last-resort safety
  net, disable with `WithoutEmergencyThreshold()`)
- LLM summarization compaction is implemented for OpenAI and Anthropic via `UseLlmSummarization(...)`
- pinned messages are implemented and survive all compaction stages
- DI registration via `AddConversationContext(...)` and factory-based creation is implemented
- OpenAI and Anthropic adapter helpers are available
- runtime recording flow is available through `SetSystemPrompt(...)`, `AddPinnedMessage(...)`, `AddUserMessage(...)`,
  `PrepareAsync(...)`, `RecordModelResponse(...)`, and `RecordToolResult(...)`

What remains planned:

- broader multi-strategy pipeline expansion beyond current masking + summarization + emergency fallback
