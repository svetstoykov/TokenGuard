<div align="center">

# TokenGuard

**Token budget management for LLM agent loops.**

[![NuGet](https://img.shields.io/nuget/v/TokenGuard.Core?style=flat-square&color=5c2d91)](https://nuget.org/packages/TokenGuard.Core)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square)](https://dotnet.microsoft.com)
[![License: MIT](https://img.shields.io/badge/License-MIT-green?style=flat-square)](LICENSE)

</div>

---

TokenGuard keeps your agent loop conversation inside `ConversationContext`. That object is the source of truth for the
session. Before each model call, TokenGuard reads that history, builds a provider-ready snapshot, and compacts only that
snapshot when needed.

```csharp
// conversationContext is source of truth for this loop.
// System prompt lives there with every other message.
conversationContext.SetSystemPrompt("You are a careful coding assistant.");

// Add user turn to same stored conversation history.
conversationContext.AddUserMessage("Fix this, make no mistake.");

// Build next provider request from that history.
// TokenGuard may compact this snapshot to fit budget.
// Stored history inside conversationContext does not change.
var prepared = await conversationContext.PrepareAsync(cancellationToken);

// Send only prepared snapshot to provider.
var input = prepared.Messages.ForOpenAI();
var response = await chatClient.CompleteChatAsync(input, cancellationToken: cancellationToken);
```

You keep appending system, user, assistant, and tool messages to `conversationContext`. Everything happens inside that
object. `PrepareAsync()` returns a `PrepareResult` describing what should go to the model right now.

---

## What it does

- **Tracks token growth** across the full turn sequence — user, assistant, tool, system, and pinned messages
- **Masks stale tool results** using a sliding-window strategy when the conversation crosses a configurable soft threshold
- **Summarizes old history with your LLM** when masking alone is not enough — collapses older turns into a compact
  summary message while keeping a recent tail verbatim
- **Falls back to emergency truncation** as a last resort — drops oldest unpinned turn groups from the prepared payload
  while preserving pinned messages and the newest active tail
- **Pins durable context** that survives all compaction stages: system prompts, task constraints, repository rules, and
  any message you need to keep unchanged
- **Stays provider-agnostic** in core, with adapter helpers for OpenAI and Anthropic
- **Integrates in minutes** via `AddConversationContext(...)` and a standard DI factory

---

## Benchmark

TokenGuard was benchmarked with Codexplorer across 20 real repository-analysis tasks from
[`samples/Codexplorer.Automation/src/tasks/initial-corpus.json`](samples/Codexplorer.Automation/src/tasks/initial-corpus.json).
The corpus spans small, medium, and large tasks, with observed runs ranging from roughly 30 to 100+ turns.

> **All 20 tasks completed successfully. TokenGuard cut cumulative prompt volume by 87.4% and prevented every context-window failure.**

| Benchmark setup | Value |
|---|---|
| Workload | 20 Codexplorer tasks across mixed difficulty levels |
| Session length | Roughly 30-100+ turns observed |
| Model | `openai/gpt-5.4-nano` |
| Context budget | 20,000 tokens |
| Soft threshold | 16,000 tokens (80%) |
| Hard cap | 20,000 tokens |
| Total turns | 1,324 |

| | Without TokenGuard | With TokenGuard |
|---|---:|---:|
| Cumulative prompt tokens | 128,058,079 | **16,158,357** |
| Tokens saved | — | **111,899,722** |
| Reduction | — | **87.4%** |
| Successful turns | — | **1,269 / 1,324 (95.8%)** |
| `CompactionInsufficient` turns | — | **55 / 1,324 (4.2%)** |
| `CannotCompact` turns | High risk on long runs | **0** |

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

Default built-in pipeline starts compaction at **80%**, always runs sliding-window masking first, and keeps LLM summarization
off until you register it explicitly.

Emergency truncation is **on by default at 1.0**. It fires only at the absolute token limit and acts as a last-resort
safety net after the normal compaction pipeline has already run.

Override with `WithEmergencyThreshold(0.95)` to trigger earlier, or call `WithoutEmergencyThreshold()` to disable it
entirely.

Multiple named profiles work too:

```csharp
services.AddConversationContext("analysis", builder => builder
    .WithMaxTokens(200_000)
    .WithCompactionThreshold(0.75));
```

Sliding-window masking is always active. Add provider-backed summarization through the provider extension packages:

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
concurrent requests. Use `Create("analysis")` when you want a named profile.

### 3. Run the loop

```csharp
using TokenGuard.Core.Enums;
using TokenGuard.Extensions.OpenAI;

var factory = serviceProvider.GetRequiredService<IConversationContextFactory>();

using var conversationContext = factory.Create();

conversationContext.SetSystemPrompt("You are a precise coding assistant.");
conversationContext.AddPinnedMessage(MessageRole.User, "Repository root is /workspace/project.");
conversationContext.AddUserMessage("Summarize the failing tests.");

while (true)
{
    var prepared = await conversationContext.PrepareAsync(cancellationToken);

    if (prepared.Outcome == PrepareOutcome.CannotCompact)
        throw new InvalidOperationException(prepared.BudgetFailureReason);

    var response = await chatClient.CompleteChatAsync(
        prepared.Messages.ForOpenAI(),
        chatOptions,
        cancellationToken);

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

`PrepareAsync()` returns a `PrepareResult`, not just a message list. `PrepareResult.Messages` is the prepared snapshot
to send to the provider. `ConversationContext.History` remains unchanged.

---

## PrepareResult

`PrepareAsync()` gives you the prepared message list plus metadata about what happened during preparation.

| Property | Meaning |
|---|---|
| `Messages` | Prepared message list to send to the provider |
| `Outcome` | `Ready`, `Compacted`, `CompactionInsufficient`, or `CannotCompact` |
| `TokensBeforeCompaction` | Estimated total before any compaction or truncation ran |
| `TokensAfterCompaction` | Estimated total of `Messages` after preparation completed |
| `MessagesCompacted` | Count of messages replaced or dropped during this call |
| `MessagesDropped` | Count of messages removed specifically by emergency truncation |
| `BudgetFailureReason` | Diagnostic text for over-budget outcomes |

`Ready` and `Compacted` are healthy outcomes. `CompactionInsufficient` means TokenGuard reduced the payload but it still
exceeds the configured limit plus any allowed overrun tolerance. `CannotCompact` means the remaining preserved content is
already too large and the call should not be attempted.

---

## Pinned messages

Some context needs to survive the whole session — task constraints, repository layout, coding standards.

```csharp
conversationContext.SetSystemPrompt("You are a senior Go engineer.");
conversationContext.AddPinnedMessage(MessageRole.User, "All file paths must be relative to /workspace.");
```

Pinned messages are never masked, never summarized, and never dropped by emergency truncation. They are removed from the
compactable slice before compaction, then reinserted at their original positions in the prepared output. They still
count against the budget.

---

## How compaction works

Want architecture detail and trade-offs? Read [How TokenGuard Thinks About Context](docs/deep-dive/context-management.md).

Three ordered tiers:

**1. Observation masking.** The sliding-window strategy walks backward through compactable history and masks older
`ToolResultContent` payloads outside the protected tail. Recent messages stay intact and structure is preserved.

**2. LLM summarization** *(opt-in — register with `UseLlmSummarization(...)`)*. If masking still leaves the compactable
history over budget, TokenGuard asks your LLM to collapse the older prefix into one summary message. The recent tail
stays verbatim. Internally, the summarization stage caches checkpoints so it can reuse or promote prior summaries instead
of regenerating them from scratch every turn.

**3. Emergency truncation** *(on by default, opt-out with `WithoutEmergencyThreshold()`)*. If the prepared request is
still above the emergency trigger after the normal compaction stages, TokenGuard drops the oldest eligible unpinned turn
groups from the prepared payload. It preserves pinned messages, summary messages, and the newest irreducible tail.

---

## Compaction statuses

`PrepareAsync()` can return two over-budget statuses after compaction work has already been attempted:

### `CompactionInsufficient`

**Meaning:** TokenGuard compacted and, if configured, also tried emergency truncation, but the prepared request still
exceeds `MaxTokens + OverrunToleranceTokens`.

**Recommended approach:** Reduce large tool-call arguments, tool outputs, or assistant payloads in the active tail;
enable LLM summarization if it is not already enabled; split the task into smaller exchanges; or increase the configured
budget only when the target provider actually supports a larger context window.

### `CannotCompact`

**Meaning:** The prepared request cannot fit because the remaining preserved content already exceeds the allowed budget
and no further messages could be compacted or dropped safely.

**Recommended approach:** Stop the exchange and reshape the input. Shorten or unpin oversized preserved content, split a
large user request or tool payload into smaller pieces, move bulky artifacts out of the live prompt, or switch to a
model with a larger real context window.

---

## LLM summarization

When masking alone is not enough, TokenGuard can replace older history with a single compact summary. The newest tail
stays verbatim. The summary is inserted as a normal `MessageRole.Model` message with `CompactionState.Summarized`.

Register it with one extra call on your builder:

```csharp
// OpenAI — model is inferred from the ChatClient
builder.UseLlmSummarization(chatClient);

// Anthropic — model must be specified explicitly
builder.UseLlmSummarization(anthropicClient, "claude-3-7-sonnet-latest");
```

Defaults keep the last **5 messages** verbatim and bound the summary budget to **2,048-4,096 tokens**. Override with
`LlmSummarizationOptions`:

```csharp
builder.UseLlmSummarization(chatClient, new LlmSummarizationOptions(
    windowSize: 5,
    minSummaryTokens: 1024,
    maxSummaryTokens: 2048));
```

| Option | What it controls | Default |
|---|---|---|
| `WindowSize` | How many newest compactable messages stay verbatim | 5 |
| `MinSummaryTokens` | Minimum remaining summary budget before the first summarization call is made | 2,048 |
| `MaxSummaryTokens` | Maximum target budget forwarded to the summarizer | 4,096 |

Only one provider per builder. Registering both OpenAI and Anthropic on the same builder throws at startup.

---

## Provider adapters

The core has no provider dependency. Adapters handle conversion in both directions.

**OpenAI**

```csharp
var prepared = await conversationContext.PrepareAsync(cancellationToken);
var messages = prepared.Messages.ForOpenAI();

var response = await chatClient.CompleteChatAsync(messages, chatOptions, cancellationToken);
conversationContext.RecordModelResponse(response.ResponseSegments(), response.InputTokens());
```

**Anthropic**

```csharp
var prepared = await conversationContext.PrepareAsync(cancellationToken);
var (messages, systemPrompt) = prepared.Messages.ForAnthropic();

// Attach both to your Anthropic request.
```

`ForOpenAI()` validates tool-call/tool-result structure and throws if the prepared history would produce orphaned tool
calls. `ForAnthropic()` returns a tuple because Anthropic carries system content separately from the normal message list.
After the Anthropic call completes, record the response with `RecordModelResponse(response.ResponseSegments())` unless your
response includes usage data. When usage is present, you can pass `response.InputTokens()` as the optional second
argument to anchor later estimates.

---

## Token counting

TokenGuard's built-in DI and factory paths always construct the heuristic `EstimatedTokenCounter`. There is no public
builder or factory hook to swap token counters at runtime yet.

Provider-reported input tokens still help when they are available:

- OpenAI: `response.InputTokens()` returns `null` when usage is absent
- Anthropic: only call `response.InputTokens()` when the SDK response includes `usage`; otherwise record the response
  without the second argument and TokenGuard stays on heuristic estimates

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

DI is the recommended path. Public factory is the manual fallback when you do not want a container.

---

## Repository layout

```
src/
  TokenGuard.Core                     core abstractions, message model, compaction pipeline
  TokenGuard.Extensions.OpenAI        OpenAI message conversion and response mapping
  TokenGuard.Extensions.Anthropic     Anthropic message conversion and response mapping

samples/
  Codexplorer                         repository-analysis sample
  Codexplorer.Automation              benchmark automation and corpus runner

tests/
  TokenGuard.Tests                    unit tests
  TokenGuard.IntegrationTests         cross-component coverage

docs/                                supporting notes and documentation
ai/skills/                           shared agent workflow guidance
```

---

## Build and test

```bash
dotnet build TokenGuard.sln --nologo
dotnet test TokenGuard.sln --no-restore --nologo
```

`TokenGuard.sln` includes the core packages, tests, and Codexplorer samples. If you only want the interactive sample:

```bash
dotnet build ./samples/Codexplorer/src/Codexplorer.csproj --nologo
```

---

## Release

Authoritative NuGet release runbook lives in [docs/release-process.md](docs/release-process.md).

Use that path for publishing `TokenGuard.Core`, `TokenGuard.Extensions.OpenAI`, and
`TokenGuard.Extensions.Anthropic`. It requires a passing
`.github/workflows/release-validation.yml` run on the exact commit being published and documents
versioning, secrets, package order, and `.snupkg` symbol publication.

---

## Requirements

- .NET SDK 10.0+
- LLM provider API key for live samples
- macOS, Linux, or Windows

---

## Current status

What is current:

- sliding-window observation masking is implemented and always part of the built-in pipeline
- built-in compaction starts at **0.80** and defaults emergency truncation to **1.0**
- emergency truncation is implemented and defaults to **1.0** as a last-resort safety net
- LLM summarization is implemented for OpenAI and Anthropic via `UseLlmSummarization(...)`
- summary checkpoint reuse and promotion are implemented inside the summarization strategy
- built-in DI and factory paths always use `EstimatedTokenCounter`; provider input-token anchoring is optional when usage
  data is available
- pinned messages survive all compaction stages
- DI registration via `AddConversationContext(...)` and factory-based creation is implemented
- OpenAI and Anthropic adapter helpers are available
- runtime recording flow is available through `SetSystemPrompt(...)`, `AddPinnedMessage(...)`, `AddUserMessage(...)`,
  `PrepareAsync(...)`, `RecordModelResponse(...)`, and `RecordToolResult(...)`

What remains planned:

- broader multi-strategy pipeline expansion beyond current masking + summarization + emergency fallback
