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
ChatCompletion response = await chatClient.CompleteChatAsync(input, cancellationToken: cancellationToken);
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

The figures below come from 20 Codexplorer sessions run on 12 May 2026 with `openai/gpt-5.4-nano`, one session per
repository-analysis task in
[`samples/Codexplorer.Automation/src/tasks/initial-corpus.json`](samples/Codexplorer.Automation/src/tasks/initial-corpus.json).
The corpus spans small, medium, and large tasks, and the sessions ran for 27 to 111 turns.

> **Across 1,325 turns, the requests TokenGuard prepared held an estimated 87.4% fewer prompt tokens than the uncompacted history of the same sessions.**

| Benchmark setup | Value |
|---|---|
| Workload | 20 Codexplorer tasks across mixed difficulty levels |
| Batch | 20 sessions, 12 May 2026 |
| Session length | 27 to 111 turns |
| Model | `openai/gpt-5.4-nano` |
| Context budget | 20,000 tokens |
| Soft threshold | 16,000 tokens (80%) |
| Hard cap | 20,000 tokens |
| Total turns | 1,325, one prepare call each |

| | Uncompacted history (estimated) | Prepared by TokenGuard (estimated) |
|---|---:|---:|
| Cumulative prompt tokens | 128,188,640 | **16,158,138** |
| Estimated tokens saved | — | **112,030,502** |
| Reduction | — | **87.4%** |
| Prepare calls with outcome `Ready` or `Compacted` | — | **1,270 / 1,325 (95.8%)** |
| Prepare calls with outcome `CompactionInsufficient` | — | **55 / 1,325 (4.2%)** |
| Prepare calls with outcome `CannotCompact` | — | **0** |

How to read these figures:

- Every session ran with TokenGuard. No run without TokenGuard was measured for this batch, so the first column is an
  estimate of the uncompacted history taken from the same sessions, not a measured control.
- Both token columns are TokenGuard's own estimates. The provider reported 15,930,640 input tokens for the prepared
  requests.
- The outcome rows count prepare calls by their `PrepareOutcome`. They say whether the prepared request fit the budget,
  not whether the model's answer was correct.
- A long session's uncompacted history is expected to outgrow a fixed context window. This batch does not measure how
  often that would have happened.

The calculation is described in
[How the benchmark figures are calculated](docs/deep-dive/context-management.md#how-the-benchmark-figures-are-calculated).

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

Register the default profile first. Registering a named profile before it creates the default profile with the built-in
settings, and a later default registration then throws `InvalidOperationException`.

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

Configuration is singleton-scoped. Each `Create()` call returns an independent stateful context, so separate contexts can
be used concurrently. A single context is for one caller at a time and nothing enforces that: if you record tool results
from parallel tool calls on one context, serialize the calls yourself. Use `Create("analysis")` when you want a named
profile.

### 3. Run the loop

The loop continues with the `conversationContext` created in step 2.

```csharp
using OpenAI.Chat;
using TokenGuard.Core.Enums;
using TokenGuard.Extensions.OpenAI;

conversationContext.SetSystemPrompt("You are a precise coding assistant.");
conversationContext.AddPinnedMessage(MessageRole.User, "Repository root is /workspace/project.");
conversationContext.AddUserMessage("Summarize the failing tests.");

while (true)
{
    var prepared = await conversationContext.PrepareAsync(cancellationToken);

    if (prepared.Outcome == PrepareOutcome.CannotCompact)
        throw new InvalidOperationException(prepared.BudgetFailureReason);

    ChatCompletion response = await chatClient.CompleteChatAsync(
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
| `TokensBeforeCompaction` | Estimated total of the recorded history before any compaction or truncation ran |
| `TokensAfterCompaction` | Estimated total of `Messages` after preparation completed. `Outcome` is decided from it |
| `MessagesCompacted` | Count of messages replaced or dropped during this call |
| `MessagesDropped` | Count of messages removed specifically by emergency truncation |
| `BudgetFailureReason` | Diagnostic text for over-budget outcomes |
| `SummarizationError` | The exception captured when LLM summarization failed during this call and TokenGuard fell back to sliding-window masking; `null` otherwise. Caller cancellation is not reported here |

Both token figures are estimates on one scale: the summed per-message estimates plus the provider correction, when one is
known. The correction is the input token count you last passed to `RecordModelResponse` minus TokenGuard's estimate of
the payload that count measured. It stays in effect, across compaction, until the next provider report replaces it. A
payload at least as large as the measured one carries the whole correction; a smaller one carries a proportional share.
On a call that changes no messages the two figures are equal.

`Ready` and `Compacted` are healthy outcomes. `Ready` is returned when the history stayed below the compaction trigger,
and also when a strategy ran but changed no messages and the result fits. `CompactionInsufficient` means TokenGuard reduced the payload but it still
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
compactable slice before compaction, then put back between the messages they were recorded between: after every
surviving message recorded before them and before every surviving message recorded after them. A pin recorded after
messages that a summary replaced follows the summary. A pin never separates a model message that carries tool calls from
its tool results; one recorded there is placed before that model message. Pinned messages still count against the
budget. When pinned messages alone exceed `MaxTokens`, `PrepareAsync()` throws `PinnedTokenBudgetExceededException`. A
pinned message cannot be removed once recorded, so size pinned content to leave room for the conversation.

---

## How compaction works

Want architecture detail and trade-offs? Read [How TokenGuard Thinks About Context](docs/deep-dive/context-management.md).

Three ordered tiers:

**1. Observation masking.** The sliding-window strategy walks backward through compactable history and masks older
`ToolResultContent` payloads outside the protected tail. Recent messages stay intact and structure is preserved.

**2. LLM summarization** *(opt-in — register with `UseLlmSummarization(...)`)*. If masking still leaves the compactable
history over budget, TokenGuard asks your LLM to collapse the older prefix into one summary message. The recent tail
stays verbatim. While the history ends with a tool result, the user message that opened that tool loop also stays
verbatim, directly after the summary. Internally, the summarization stage caches checkpoints so it can reuse or promote
prior summaries instead of regenerating them from scratch every turn.

**3. Emergency truncation** *(on by default, opt-out with `WithoutEmergencyThreshold()`)*. If the prepared request is
still above the emergency trigger after the normal compaction stages, TokenGuard drops the oldest eligible unpinned turn
groups from the prepared payload. It preserves pinned messages, a summary message and everything after it, and the
newest message. When the newest message is a tool result, it also preserves the model message that made the tool call
and the user message that opened that tool loop, so the model still sees the request it is working on.

A turn group is one unpinned user message and everything recorded after it up to the next unpinned user message: the
model replies, tool calls, and tool results that answer it. Groups come from the messages themselves, so a history
recorded in one go is grouped like the same history recorded turn by turn. An older group is dropped whole. The newest
group, still in progress, keeps its user message and loses its oldest tool exchanges first, and a tool call always goes
together with its results. If the preserved messages alone exceed the budget, the outcome is `CompactionInsufficient` or
`CannotCompact`, never `Compacted`.

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

ChatCompletion response = await chatClient.CompleteChatAsync(messages, chatOptions, cancellationToken);
conversationContext.RecordModelResponse(response.ResponseSegments(), response.InputTokens());
```

**Anthropic**

```csharp
var prepared = await conversationContext.PrepareAsync(cancellationToken);
var (messages, systemPrompt) = prepared.Messages.ForAnthropic();

// Attach both to your Anthropic request.
```

`ForOpenAI()` sends every text segment of a message as its own content part and throws `InvalidOperationException` for a
message it cannot represent, for an orphaned tool result, and for tool calls left without results, including at the end
of the list. `ForAnthropic()` returns a tuple because Anthropic carries system content separately from the normal message list.
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

## Observability

Logs go to the container's `ILoggerFactory` for contexts created by `IConversationContextFactory` resolved from the
container, or to `WithLoggerFactory(...)` without DI. Tracing and metrics subscribe by name:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource(TokenGuardDiagnostics.ActivitySourceName))
    .WithMetrics(m => m.AddMeter(TokenGuardDiagnostics.MeterName));
```
None of it contains conversation content. See [Observability](docs/observability.md) for event IDs, instruments, and levels.

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
  TokenGuard.ReadmeSnippets           README quick start compiled against packed packages

docs/                                supporting notes and documentation
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

Use that path to publish `TokenGuard.Core`, `TokenGuard.Extensions.OpenAI`, or
`TokenGuard.Extensions.Anthropic` from an existing Git tag. Publication runs manually through
GitHub Actions, publishes one selected package per run, and authenticates to nuget.org through
Trusted Publishing without a long-lived API key.

---

## Requirements

- .NET SDK 10.0+
- LLM provider API key for live samples
- Linux and macOS. Windows is expected to work and is not covered by CI

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
