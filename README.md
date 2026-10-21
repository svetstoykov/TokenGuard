<div align="center">

# TokenGuard

**Context management for .NET agent harnesses.**

[![Build](https://img.shields.io/github/actions/workflow/status/svetstoykov/TokenGuard/release-validation.yml?branch=main&style=flat-square&label=build)](https://github.com/svetstoykov/TokenGuard/actions/workflows/release-validation.yml)
[![NuGet](https://img.shields.io/nuget/v/TokenGuard.Core?style=flat-square&color=5c2d91)](https://nuget.org/packages/TokenGuard.Core)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square)](https://dotnet.microsoft.com)
[![License: MIT](https://img.shields.io/badge/License-MIT-green?style=flat-square)](LICENSE)

</div>

---

You write the agent loop; TokenGuard keeps its context inside the model's window. It holds the conversation history,
counts tokens, and before each model call builds a request that fits: stale tool results masked, old turns summarized by
your own LLM, pinned instructions untouched. It plugs into OpenAI, Anthropic and Microsoft.Extensions.AI.

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
- **Shortens an oversized tool result** when it is recorded, so one large output cannot fill the window
- **Masks stale tool results** using a sliding-window strategy when the conversation crosses the compaction threshold
- **Summarizes old history with your LLM** when masking alone is not enough — collapses older turns into a compact
  summary message while keeping a recent tail verbatim
- **Falls back to emergency truncation** as a last resort — drops oldest unpinned turn groups from the prepared payload
  while preserving pinned messages and the newest active tail
- **Pins durable context** that survives all compaction stages: system prompts, task constraints, repository rules, and
  any message you need to keep unchanged
- **Stays provider-agnostic** in core, with adapter helpers for OpenAI, Anthropic and Microsoft.Extensions.AI
- **Integrates in minutes** via `AddConversationContext(...)` and a standard DI factory

---

## Terms

| Term | Meaning | Set with |
|---|---|---|
| Budget | The input token limit for one prepared request, with no room reserved for the reply. The benchmark section calls it the window | `WithMaxTokens(...)`; 40,000 when `AddConversationContext()` is called without a builder |
| Compaction threshold | The fraction of the budget at which compaction starts | `WithCompactionThreshold(...)`, default 0.80 |
| Emergency threshold | The fraction of the budget at which emergency truncation starts to drop the oldest turn groups | `WithEmergencyThreshold(...)`, default 1.0; `WithoutEmergencyThreshold()` turns it off |
| Tool result limit | The largest estimated token count one tool result is stored at; a larger result is stored shortened | `WithMaxToolResultTokens(...)`, default the smaller of 25,000 and a quarter of the budget; `WithoutToolResultLimit()` turns it off |
| Masking window | The newest messages that masking leaves unchanged, as a minimum count of messages | `SlidingWindowOptions.WindowSize`, default 10, passed to `WithSlidingWindowOptions(...)` |
| Summary tail | The newest messages that stay verbatim after a summary, as a count of messages | `LlmSummarizationOptions.WindowSize`, default 5, passed to `UseLlmSummarization(...)` |

---

## Benchmark

**About 70% fewer input tokens in long agent sessions.** The agent still recorded 55 of 59 facts correctly, against 58
of 59 without TokenGuard.

Input tokens sent to the model by the same agent on the same tasks, with and without TokenGuard:

| Session | Window | Without TokenGuard | With TokenGuard | Saved |
|---|---:|---|---|---:|
| 100k tokens of history | 50k | `████████████████████` 10.3M | `█████████▌` 4.9M | **53%** |
| 200k tokens of history | 65k | `████████████████████` 36.1M | `██████▌` 11.6M | **68%** |
| 300k tokens of history | 100k | `████████████████████` 91.8M | `█████▌` 25.3M | **72%** |
| Short tasks, ≤ 58 calls | 50k | `████████████████████` 18.3M | `███████████` 10.1–10.8M | **41–45%** |

- **The longer the session, the bigger the saving.** Nothing is masked until the history reaches the compaction
  threshold, so short sessions save less and a session that stays below it saves nothing.
- **The bill drops too: $1.52 instead of $5.46** across the long sessions. Masking keeps most of each request identical
  to the previous one, so 80% of the input was billed at the provider's cache-read price.
- **Re-measured on 1.3.0** at 200k and 300k: 70% and 73% fewer input tokens.

<details>
<summary>How it was measured, and the limits</summary>

- The Codexplorer sample agent with `qwen/qwen3.7-flash` on one provider. The control run uses a 1,000,000-token window,
  so nothing is compacted; the treatment compacts at 80% of the stated window. Summarizer input is counted.
- Long sessions: six edit-and-re-read tasks, two per history size, on TokenGuard 1.2.0, one run per arm. Short tasks:
  ten tasks from [`verifiable-corpus.json`](samples/Codexplorer.Automation/src/tasks/verifiable-corpus.json) on
  TokenGuard 1.1.0, three treatment runs against one control. The treatments passed 31, 31 and 29 of 31 checks, the
  control 31 of 31.
- A window that is too small costs correctness: at 20k the short tasks lost 10 of 31 checks, at 50k they held. Size the
  window to hold the largest single turn of tool results plus pinned content, with room left for history.
- One tool result larger than the window cannot be fitted. At the 50k window three `grep` results of about 260,000
  tokens each went out over the window, and the prepare returned `CompactionInsufficient`.
- The money figure depends on the provider's cache pricing.

Run IDs and the full method are in
[Benchmark results](docs/deep-dive/context-management.md#14-benchmark-results).

</details>

---

## Install

```bash
dotnet add package TokenGuard.Core
dotnet add package TokenGuard.Extensions.OpenAI
dotnet add package Microsoft.Extensions.DependencyInjection
```

For Anthropic, add `TokenGuard.Extensions.Anthropic` in place of `TokenGuard.Extensions.OpenAI`:

```bash
dotnet add package TokenGuard.Core
dotnet add package TokenGuard.Extensions.Anthropic
dotnet add package Microsoft.Extensions.DependencyInjection
```

`Microsoft.Extensions.DependencyInjection` supplies the `ServiceCollection` that both quick starts register TokenGuard
in.

---

## Quick start

The program below is a complete agent loop against OpenAI with one tool. The same program for Anthropic is under
[Provider adapters](#provider-adapters). Create a console project and add three packages:

```bash
dotnet new console -n TokenGuardQuickStart
cd TokenGuardQuickStart
dotnet add package TokenGuard.Core
dotnet add package TokenGuard.Extensions.OpenAI
dotnet add package Microsoft.Extensions.DependencyInjection
```

Replace the contents of `Program.cs` with the program, set the `OPENAI_API_KEY` environment variable, and run
`dotnet run`. The OpenAI SDK arrives through `TokenGuard.Extensions.OpenAI`.

```csharp
using Microsoft.Extensions.DependencyInjection;
using OpenAI.Chat;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Extensions;
using TokenGuard.Extensions.OpenAI;

var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
    ?? throw new InvalidOperationException("Set the OPENAI_API_KEY environment variable.");
var chatClient = new ChatClient("gpt-5.4-nano", apiKey);
var chatOptions = new ChatCompletionOptions
{
    Tools = { ChatTool.CreateFunctionTool("list_failing_tests", "Lists the tests that fail in the repository.") },
};

var services = new ServiceCollection();
services.AddConversationContext(builder => builder
    .WithMaxTokens(40_000)
    .WithCompactionThreshold(0.80));

using var serviceProvider = services.BuildServiceProvider();
using var conversationContext = serviceProvider
    .GetRequiredService<IConversationContextFactory>()
    .Create();

conversationContext.SetSystemPrompt("You are a precise coding assistant.");
conversationContext.AddPinnedMessage(MessageRole.User, "Repository root is /workspace/project.");
conversationContext.AddUserMessage("Summarize the failing tests.");

const int maxTurns = 20;

for (var turn = 1; turn <= maxTurns; turn++)
{
    var prepared = await conversationContext.PrepareAsync();

    // Both outcomes mean the prepared request is still over the budget, so stop before the provider rejects it.
    if (prepared.Outcome is PrepareOutcome.CompactionInsufficient or PrepareOutcome.CannotCompact)
        throw new InvalidOperationException(prepared.BudgetFailureReason);

    ChatCompletion response = await chatClient.CompleteChatAsync(prepared.Messages.ForOpenAI(), chatOptions);
    var segments = response.ResponseSegments();

    // A reply with no text and no tool call cannot be recorded: RecordModelResponse throws for it.
    if (segments.Count == 0)
        throw new InvalidOperationException("The model returned an empty reply.");

    conversationContext.RecordModelResponse(segments, response.InputTokens());

    if (response.ToolCalls.Count == 0)
    {
        Console.WriteLine(string.Join(Environment.NewLine, response.TextSegments().Select(segment => segment.Content)));
        return;
    }

    foreach (var toolCall in response.ToolCalls)
    {
        var result = ExecuteTool(toolCall);
        conversationContext.RecordToolResult(toolCall.Id, toolCall.FunctionName, result);
    }
}

Console.WriteLine($"Stopped after {maxTurns} model calls without a final answer.");

static string ExecuteTool(ChatToolCall toolCall) => toolCall.FunctionName switch
{
    "list_failing_tests" => "OrderTests.Total_IncludesTax failed: expected 108.00 but was 100.00.",
    _ => $"Unknown tool {toolCall.FunctionName}.",
};
```

`PrepareAsync()` returns a `PrepareResult`, not just a message list. `PrepareResult.Messages` is the prepared snapshot
to send to the provider. `ConversationContext.History` remains unchanged.

`PrepareAsync()` returns what was recorded, so a context with no messages gives an empty `Ready` result: record at least
one user message before the first model call.

The loop stops in four ways. It returns when the model answers without a tool call. It throws when the outcome is
`CompactionInsufficient` or `CannotCompact`, because in both the prepared request is still over the budget;
[Compaction statuses](#compaction-statuses) describes each and what to change. It throws when the model returns an
empty reply. It ends after `maxTurns` model calls, so a model that keeps calling tools cannot run without a limit.

An empty reply, one with no text and no tool call, is not recorded: `RecordModelResponse` throws `ArgumentException`
for a reply with no segments. Check the segments first and decide whether to retry the call or stop.

### Register at startup

The program registers a 40,000-token budget with `AddConversationContext(...)`, so compaction starts at 32,000 tokens.

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
    .WithMaxTokens(40_000)
    .WithSlidingWindowOptions(new SlidingWindowOptions(windowSize: 12))
    .UseLlmSummarization(chatClient));
```

```csharp
services.AddConversationContext(builder => builder
    .WithMaxTokens(40_000)
    .UseLlmSummarization(anthropicClient, "claude-sonnet-5-5"));
```

### Choose a budget

The budget, `MaxTokens`, is the input budget of one request: TokenGuard reserves nothing for the reply. Set it so that
the model's context window minus the largest reply you request is at least `MaxTokens × (1 + overrun tolerance)`; the
provider takes the output limit from the same context window, and a prepared request may run over `MaxTokens` by the
tolerance. Request overhead such as tool schemas is part of the input but not of the estimate. Once you pass a
provider-reported input count to `RecordModelResponse`, the correction includes it. If you never pass one, leave room
for the tool definitions as well, about 140 tokens per tool in the measured runs. From below, the budget must hold the
largest single turn of tool results plus the pinned content, with room left for history. Two points are measured, on the short
verifiable tasks of the [benchmark](#benchmark): a budget of 20,000 tokens lost 10 of 31 checks, and 50,000 tokens
held.

### Create a context per conversation

Configuration is singleton-scoped. Each `Create()` call returns an independent stateful context, so separate contexts can
be used concurrently. A single context is for one caller at a time: a call that starts while another call on the same
context is still running throws `InvalidOperationException`, on a best-effort basis. Run parallel tool calls in parallel
and record their results one after another. Use `Create("analysis")` when you want a named profile.

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
| `ToolResultsMasked` | Tool-result segments replaced in the selected masking output before emergency truncation |
| `MessagesSummarized` | Input messages replaced by the selected summary, including checkpoint reuse |
| `SummarizationStatus` | `NotNeeded`, `CheckpointReused`, `NewSummary`, `Failed`, or `SkippedPreviousFailure` |
| `UnchangedPrefixMessages` | Leading messages identical to the previous successfully returned view; zero on the first prepare |
| `UnchangedPrefixTokens` | Tokens of that prefix under the current provider correction; zero on the first prepare |
| `TokenBreakdown` | Provider-corrected `Pinned`, `Summary`, `Masked`, `ToolResults`, and `Other` tokens in the final view |
| `BudgetFailureReason` | Diagnostic text for over-budget outcomes |
| `SummarizationError` | The exception captured when LLM summarization failed during this call and TokenGuard fell back to sliding-window masking; `null` otherwise. Caller cancellation is not reported here |

Both token figures are estimates on one scale: the summed per-message estimates plus the provider correction, when one is
known. The correction is the input token count you last passed to `RecordModelResponse` minus TokenGuard's estimate of
the payload that count measured. It stays in effect, across compaction, until the next provider report replaces it. A
payload at least as large as the measured one carries the whole correction; a smaller one carries a proportional share.
On a call that changes no messages the two figures are equal.

Stage counts describe the selected strategy output. Trial masking discarded in favor of a summary is excluded; a masked
result subsequently removed by emergency truncation remains in `ToolResultsMasked` and also contributes to
`MessagesDropped`. `TokenBreakdown` describes only the final view. It assigns whole messages in this precedence:
pinned, summarized, masked, containing tool results, then other. A mixed masked message includes retained exempt results
and text in `Masked`. The five categories include framing and proportional provider correction, with rounding allocated
at cumulative category boundaries so they sum exactly to `TokensAfterCompaction`. When every message estimate is zero,
any provider correction belongs to the first message's category, or `Other` for an empty view.

`Failed` includes a thrown summarizer call or a rejected summary above the budget; only a thrown failure supplies
`SummarizationError`. `NotNeeded` also covers a missing summarizer or insufficient room for a useful first summary.
`MessagesSummarized` excludes a user message retained verbatim to keep an open tool loop valid.

Prefix equality compares the role and ordered segment values, including tool call identifiers and names. Timestamps,
pinning, compaction state, and cached counts are observational metadata. The context keeps one private copied snapshot
of the last successful view, using linear space in its messages and segments. Comparison is linear in the compared
content; this reporting state never changes masking boundaries or strategy inputs. The prefix is a payload stability
measure; provider framing, tools, and cache rules still determine actual cache hits.

The result constructors keep their existing signatures. Additional properties are init-only with zero counts, an empty
token breakdown, and `NotNeeded` as defaults, so custom strategies can opt into stage reporting incrementally.

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

Before any tier runs, `RecordToolResult` applies the tool result limit. A result whose estimated tokens are above it is
stored shortened: whole lines from its start and its end, about half of the limit each, with a notice in place of the
omitted middle:
`[Tool result shortened to fit the context: 1840 of 2100 lines omitted here. Call the tool again with a narrower request if you need them.]`
Text with one line is cut by characters. The limit defaults to the smaller of 25,000 and a quarter of the budget (10,000
at 40,000), because a result of the tool loop in progress can be neither masked nor dropped, and one larger than the
window would otherwise leave every prepare call over budget. A tool that bounds its own output below the limit is never
cut. Set the limit with `WithMaxToolResultTokens(...)`, or call `WithoutToolResultLimit()` to store every result whole.
If the cap cannot fit the omission notice itself, the full notice is stored alone and may exceed the cap.
The context does not keep the full text of a shortened result; keep it yourself if you need it.

Three ordered tiers:

**1. Observation masking.** The sliding-window strategy walks backward through compactable history and masks older
`ToolResultContent` payloads outside the protected tail. Recent messages stay intact and structure is preserved. A tool
result that costs no more tokens than its placeholder, such as `ok` or an exit code, keeps its content. Results of the
tools named in `SlidingWindowOptions.ExemptToolNames`, such as a notes or todo-list tool the agent relies on later, are
never masked: `new SlidingWindowOptions(windowSize: 12) { ExemptToolNames = ["todo_write"] }`. Names match exactly,
case included. An exempt result keeps its recorded size, so large ones leave masking less to reclaim.
The tool result limit applies when recording exempt tools too.

A masked tool result is gone from the request, and the model can get it back only by calling the tool again. Say so in
your system prompt, and do not tell the model to avoid re-reading: the default placeholder reads
`[Tool result removed to save context — read_file, call_abc123. Call the tool again if you need this content.]`

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
together with its results. If the preserved messages alone exceed `MaxTokens` by more than the overrun tolerance (5% of
`MaxTokens` by default, set with `WithOverrunTolerance(...)`), the outcome is `CompactionInsufficient`, or
`CannotCompact` when nothing was masked, summarized, or dropped. An overrun within the tolerance counts as fitting, and
the outcome is `Compacted`, or `Ready` when no message changed.

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
builder.UseLlmSummarization(anthropicClient, "claude-sonnet-5-5");
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
| `SummaryInstructions` | Replacement summary instructions; fixed transcript guidance is always sent | `null`, using `DefaultSummaryInstructions` |

Only one summarizer per builder. Registering a second built-in or custom summarizer throws at startup.

TokenGuard sets no time limit on a summarizer call. The call is bounded only by the cancellation token you pass to
`PrepareAsync()` and by the network timeout of the provider client you registered. Pass a token, or set the client's
timeout, if a slow summary must not hold up your loop.

When a summarizer call fails, or returns a summary that is too large for the budget, TokenGuard falls back to the masked
history and does not ask again while the history and the budget stay the same. Calling `PrepareAsync()` again without
recording a message makes no second summarizer request, and `SummarizationError` is set only on the call that made the
request. The next recorded message allows a new attempt. A call that you cancel is not counted as a failed attempt.

### Custom summarizer

Implement `TokenGuard.Core.Abstractions.ILlmSummarizer` to use another model client. Core builds the prompts, bounds the
transcript, and checks the answer. The implementation sends `SystemPrompt` as instructions, `UserPrompt` as one user
message, and `MaxOutputTokens` as the output cap, then maps its provider's text, finish reason, and optional usage to
`SummarizerResponse`. For example, a callback around your model client can be registered through this implementation:

```csharp
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Summarization;

sealed class CustomSummarizer(
    Func<SummarizerRequest, CancellationToken, Task<SummarizerResponse>> callModel) : ILlmSummarizer
{
    public Task<SummarizerResponse> SummarizeAsync(
        SummarizerRequest request, CancellationToken cancellationToken = default)
    {
        return callModel(request, cancellationToken);
    }
}
```

Register an instance with `builder.UseLlmSummarization(new CustomSummarizer(callModel))`, where `callModel` performs your
provider call and returns a `SummarizerResponse`. The instance is shared by every context of the configuration and must
be safe for concurrent use. `Completed` and `Other` accept non-blank text; `OutputLimit` accepts it and logs warning 5003;
`Refused` and blank text are failed summaries. Exceptions follow the same fallback and remembered-attempt rules above.

Set `SummaryInstructions` in `LlmSummarizationOptions` to replace the summary instructions, or start from
`LlmSummarizationOptions.DefaultSummaryInstructions`. Empty or whitespace instructions throw. The transcript legend,
the token-limit guidance, and the rule that the transcript is data stay fixed. Default options preserve the existing
system and user prompts. Custom instructions are followed by the fixed guidance; longer instructions leave less room
for the transcript.

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

The program below is a complete agent loop against Anthropic with one tool. Create a console project and add three
packages:

```bash
dotnet new console -n TokenGuardAnthropicQuickStart
cd TokenGuardAnthropicQuickStart
dotnet add package TokenGuard.Core
dotnet add package TokenGuard.Extensions.Anthropic
dotnet add package Microsoft.Extensions.DependencyInjection
```

Replace the contents of `Program.cs` with the program, set the `ANTHROPIC_API_KEY` environment variable, and run
`dotnet run`. The Anthropic SDK arrives through `TokenGuard.Extensions.Anthropic`. `UseLlmSummarization(...)` is
optional; without it TokenGuard masks old tool results and does not call a summarizer.

```csharp
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.DependencyInjection;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Extensions;
using TokenGuard.Extensions.Anthropic;

AnthropicClient anthropicClient = new(); // Reads the ANTHROPIC_API_KEY environment variable.
var cancellationToken = CancellationToken.None;

var services = new ServiceCollection();
services.AddConversationContext(builder => builder
    .WithMaxTokens(40_000)
    .UseLlmSummarization(anthropicClient, "claude-opus-5-5"));

using var serviceProvider = services.BuildServiceProvider();
using var conversationContext = serviceProvider
    .GetRequiredService<IConversationContextFactory>()
    .Create();

Tool readFile = new()
{
    Name = "read_file",
    Description = "Reads a text file from the repository.",
    InputSchema = new()
    {
        Properties = new Dictionary<string, JsonElement>
        {
            ["path"] = JsonSerializer.SerializeToElement(new { type = "string", description = "Path from the repository root." }),
        },
        Required = ["path"],
    },
};

conversationContext.SetSystemPrompt("You are a precise coding assistant.");
conversationContext.AddPinnedMessage(MessageRole.User, "Repository root is /workspace/project.");
conversationContext.AddUserMessage("Summarize the failing tests.");

const int maxTurns = 20;

for (var turn = 1; turn <= maxTurns; turn++)
{
    var prepared = await conversationContext.PrepareAsync(cancellationToken);

    // Both outcomes mean the prepared request is still over the budget, so stop before the provider rejects it.
    if (prepared.Outcome is PrepareOutcome.CompactionInsufficient or PrepareOutcome.CannotCompact)
        throw new InvalidOperationException(prepared.BudgetFailureReason);

    var (messages, systemPrompt) = prepared.Messages.ForAnthropic();

    Message response = await anthropicClient.Messages.Create(
        new MessageCreateParams
        {
            Model = "claude-opus-5-5",
            MaxTokens = 16_000,
            System = systemPrompt,
            Messages = messages,
            Tools = [readFile],
        },
        cancellationToken);
    var segments = response.ResponseSegments();

    // A reply with no text and no tool call cannot be recorded: RecordModelResponse throws for it.
    if (segments.Count == 0)
        throw new InvalidOperationException("The model returned an empty reply.");

    conversationContext.RecordModelResponse(segments, response.InputTokens());

    var toolCalls = response.ToolUseSegments();

    if (toolCalls.Count == 0)
    {
        Console.WriteLine(string.Join(Environment.NewLine, response.TextSegments().Select(segment => segment.Content)));
        return;
    }

    foreach (var toolCall in toolCalls)
    {
        var result = ExecuteTool(toolCall.ToolName, toolCall.Content);
        conversationContext.RecordToolResult(toolCall.ToolCallId, toolCall.ToolName, result);
    }
}

Console.WriteLine($"Stopped after {maxTurns} model calls without a final answer.");

// Replace with your own tool dispatch. The arguments arrive as a JSON object.
static string ExecuteTool(string toolName, string argumentsJson) => $"Executed {toolName} with {argumentsJson}.";
```

`ForOpenAI()` sends every text segment of a message as its own content part and throws `InvalidOperationException` for a
message it cannot represent, for an orphaned tool result, and for tool calls left without results, including at the end
of the list.

`ForAnthropic()` follows the same rule: it sends every text segment as its own text block and every result of a tool
message, and throws `InvalidOperationException` for a message it cannot represent, for tool-call arguments that are not a
JSON object, for an orphaned tool result, and for tool calls left without results. It returns a tuple because Anthropic
carries system content separately from the message list; the text of every system message goes to that system value.
Thinking and redacted-thinking blocks in a response are not recorded and not sent back, so compaction cannot invalidate
one, and the model does not see its reasoning from earlier turns. The Anthropic adapter is covered by unit tests and was
run by hand against the Anthropic API in October 2026 on Claude Haiku 5.5, Sonnet 5.5, Opus 5.5 and Fable 5.1; the
[package readme](src/TokenGuard.Extensions.Anthropic/PackageReadme.md) lists what those runs showed and what is still
not verified.

### Microsoft.Extensions.AI

Install `TokenGuard.Extensions.AI` and your provider's `IChatClient` implementation. The adapter depends on
`Microsoft.Extensions.AI.Abstractions` 10.11.0 or later and targets .NET 10. Keep one context per conversation and one
caller at a time. This helper accepts your client and tool declarations:

```csharp
using Microsoft.Extensions.AI;
using TokenGuard.Core;
using TokenGuard.Core.Configuration;
using TokenGuard.Core.Enums;
using TokenGuard.Extensions.AI;
```

```csharp
// Supply your provider's IChatClient and any tool declarations in options.
// Keep this client alive for the lifetime of its contexts.
static async Task RunAsync(IChatClient chatClient, ChatOptions options, CancellationToken cancellationToken = default)
{
    var configuration = new ConversationConfigBuilder()
        .WithMaxTokens(40_000)
        .UseLlmSummarization(chatClient)
        .Build();
    using var context = new ConversationContextFactory(configuration).Create();
    context.SetSystemPrompt("You are a precise coding assistant.");
    context.AddPinnedMessage(MessageRole.User, "Repository root is /workspace/project.");
    context.AddUserMessage("Summarize the failing tests.");

    for (var turn = 0; turn < 20; turn++)
    {
        var prepared = await context.PrepareAsync(cancellationToken);
        if (prepared.Outcome is PrepareOutcome.CompactionInsufficient or PrepareOutcome.CannotCompact)
            throw new InvalidOperationException(prepared.BudgetFailureReason);

        var response = await chatClient.GetResponseAsync(prepared.Messages.ForChatClient(), options, cancellationToken);
        var segments = response.ResponseSegments();
        if (segments.Count == 0)
            throw new InvalidOperationException("The model returned an empty reply.");

        context.RecordModelResponse(segments, response.InputTokens());
        var toolCalls = response.ToolUseSegments();
        if (toolCalls.Count == 0)
        {
            Console.WriteLine(string.Join(Environment.NewLine, response.TextSegments().Select(segment => segment.Content)));
            return;
        }

        foreach (var toolCall in toolCalls)
        {
            var result = ExecuteTool(toolCall.ToolName, toolCall.Content);
            context.RecordToolResult(toolCall.ToolCallId, toolCall.ToolName, result);
        }
    }

    throw new InvalidOperationException("Stopped after 20 model calls without a final answer.");

    // Replace with your own dispatch; argumentsJson holds the tool's JSON object.
    static string ExecuteTool(string name, string argumentsJson) => $"Executed {name} with {argumentsJson}.";
}
```

Drive each tool round in the caller, as above. `FunctionInvokingChatClient` returns intermediate function results and
aggregates usage across rounds; TokenGuard rejects responses containing `FunctionResultContent` with guidance to
remove automatic function invocation. This keeps the recorded history and provider correction aligned with each request.
See [Microsoft's implementation](https://github.com/dotnet/extensions/blob/v10.11.0/src/Libraries/Microsoft.Extensions.AI/ChatCompletion/FunctionInvokingChatClient.cs).

Leave `ChatOptions.ConversationId` unset and configure the provider client to send the complete prepared history;
server-managed history can retain content that TokenGuard compacted. For streaming, collect the updates with
`ToChatResponseAsync()` before using these response helpers. Text and function calls are supported; reasoning content
and usage content are omitted, and unsupported content such as media throws `NotSupportedException`. Function calls with
argument-mapping failures or marked informational throw `InvalidOperationException` rather than becoming executable tool requests.

TokenGuard keeps the history, correction and summary checkpoint in the caller's context. Its token budget uses provider
usage correction, masks old tool results in steps, and preserves pinned messages through every compaction stage.
The experimental Microsoft.Extensions.AI [chat reducers](https://github.com/dotnet/extensions/tree/v10.11.0/src/Libraries/Microsoft.Extensions.AI/ChatReduction)
reduce lists using message counts. [Agent Framework compaction](https://learn.microsoft.com/agent-framework/concepts/agents/conversations/compaction)
also supports token, turn and group triggers. This adapter supplies conversions and summarization for a caller-owned
TokenGuard context; it provides no middleware or `IChatReducer` registration.

---

## Custom token counter and strategy

Share a counter with the context so your tools and the context measure on the same scale:

```csharp
var counter = new EstimatedTokenCounter(TokenCountSafetyMode.Safe);
var config = new ConversationConfigBuilder()
    .WithMaxTokens(40_000)
    .WithTokenCounter(counter)
    .Build();
```

`EstimatedTokenCounter` is in `TokenGuard.Core.TokenCounting`, and `TokenCountSafetyMode` is in `TokenGuard.Core.Enums`.
You can also implement `ITokenCounter` from `TokenGuard.Core.Abstractions`. One instance serves all contexts created from
that configuration and must be safe for concurrent use. It measures recorded messages, tool-result limits, compaction
and summarizer requests. Provider correction applies on top of its counts.

Supply a factory for a fresh `ICompactionStrategy` per conversation:

```csharp
var config = new ConversationConfigBuilder()
    .WithMaxTokens(40_000)
    .WithCompactionStrategy(context => new MyCompactionStrategy(
        context.TokenCounter,
        context.LoggerFactory.CreateLogger<MyCompactionStrategy>()))
    .Build();
```

`MyCompactionStrategy` is your implementation of `ICompactionStrategy`. Its factory receives a `CompactionStrategyContext`
from `TokenGuard.Core.Models`, with the timed counter, resolved logger factory and configuration name. The logger factory
uses `WithLoggerFactory`, then the DI container's factory, then a factory that writes no records. Include
`Microsoft.Extensions.Logging` for `CreateLogger<T>()`.

A custom strategy replaces the built-in masking and summarization pipeline. Combining it with `WithSlidingWindowOptions`
or `UseLlmSummarization` throws at `Build()`. Register a custom summarizer instead when you want to keep the built-in pipeline.

---

## Token counting

TokenGuard uses the public, thread-safe `EstimatedTokenCounter` by default, with `TokenCountSafetyMode.Balanced`.
Set a shared custom counter with `WithTokenCounter(ITokenCounter)`. Estimates can change between versions.

The estimator counts text that parses as JSON from its structure, plus a charge for the whitespace between its tokens
that was fitted to the `o200k_base` tokenizer.

Provider-reported input tokens still help when they are available. The difference between the reported count and the
estimate becomes the provider correction: the compaction trigger and the reported totals include it, and the budget
passed to masking and summarization is converted by the ratio of the history's estimate to its corrected total. The
adapters read the reported count from a response:

- OpenAI: `response.InputTokens()` returns `null` when usage is absent
- Anthropic: `response.InputTokens()` returns the total input of the request, the sum of `input_tokens`,
  `cache_creation_input_tokens` and `cache_read_input_tokens`, so the value stays correct when prompt caching serves
  part of the request. It returns `null` when usage is absent

---

## Observability

Logs go to the container's `ILoggerFactory` for contexts created by `IConversationContextFactory` resolved from the
container, or to `WithLoggerFactory(...)` without DI. Tracing and metrics subscribe by name.

TokenGuard prints nothing by itself. The program below writes its log records, activities, and metrics to the console
without calling a model. Besides `TokenGuard.Core` and `Microsoft.Extensions.DependencyInjection` it needs the console
logging provider, `Microsoft.Extensions.Logging.Console`, and the OpenTelemetry console exporter,
`OpenTelemetry.Exporter.Console`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Diagnostics;
using TokenGuard.Core.Extensions;

using var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .AddSource(TokenGuardDiagnostics.ActivitySourceName)
    .AddConsoleExporter()
    .Build();
using var meterProvider = Sdk.CreateMeterProviderBuilder()
    .AddMeter(TokenGuardDiagnostics.MeterName)
    .AddConsoleExporter()
    .Build();

var services = new ServiceCollection();
services.AddLogging(logging => logging.AddConsole().SetMinimumLevel(LogLevel.Debug));
services.AddConversationContext(builder => builder.WithMaxTokens(40_000));

using var serviceProvider = services.BuildServiceProvider();
using var conversationContext = serviceProvider.GetRequiredService<IConversationContextFactory>().Create();

conversationContext.AddUserMessage("Summarize the failing tests.");
await conversationContext.PrepareAsync();
```

None of it contains conversation content. See [Observability](docs/observability.md) for event IDs, instruments, and levels.

## Without DI

If you're not using a container, construct a factory directly:

```csharp
var factory = new ConversationContextFactory(
    new ConversationConfigBuilder()
        .WithMaxTokens(40_000)
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
  TokenGuard.Extensions.AI            Microsoft.Extensions.AI conversion and summarization

samples/
  Codexplorer                         repository-analysis sample
  Codexplorer.Automation              benchmark automation and corpus runner

tests/
  TokenGuard.Tests                    unit tests
  TokenGuard.IntegrationTests         cross-component coverage
  Codexplorer.Automation.Tests        tests for the benchmark automation
  TokenGuard.ReadmeSnippets           README quick start compiled against packed packages

docs/                                 observability guide, release runbook, context-management deep dive
```

---

## Build and test

```bash
dotnet build TokenGuard.sln --nologo
dotnet test TokenGuard.sln --no-restore --nologo
```

`TokenGuard.sln` includes the four packages, the two Codexplorer sample projects, and the test projects `TokenGuard.Tests`, `TokenGuard.IntegrationTests` and `Codexplorer.Automation.Tests`. `tests/TokenGuard.ReadmeSnippets` is outside the solution; run it with `./tests/TokenGuard.ReadmeSnippets/verify.sh`. See [CONTRIBUTING.md](CONTRIBUTING.md) for the contribution workflow.

If you only want the interactive sample:

```bash
dotnet build ./samples/Codexplorer/src/Codexplorer.csproj --nologo
```

---

## Release

Authoritative NuGet release runbook lives in [docs/release-process.md](docs/release-process.md).

Use that path to publish `TokenGuard.Core`, `TokenGuard.Extensions.OpenAI`,
`TokenGuard.Extensions.Anthropic`, or `TokenGuard.Extensions.AI` from an existing Git tag. Publication runs manually through
GitHub Actions, publishes one selected package per run, and authenticates to nuget.org through
Trusted Publishing without a long-lived API key.

---

## Requirements

- .NET SDK 10.0+
- LLM provider API key for live samples
- The packages target `net10.0` only. .NET 8 and .NET 9 both reach end of support on November 10, 2026; .NET 10 is a long-term support release, supported until November 14, 2028 ([.NET support policy](https://dotnet.microsoft.com/platform/support/policy/dotnet-core))
- CI covers Linux only (`ubuntu-latest`). macOS and Windows are expected to work and are not covered by CI

---

## Current status

What is current:

- sliding-window observation masking is implemented and always part of the built-in pipeline
- built-in compaction starts at **0.80** and defaults emergency truncation to **1.0**
- emergency truncation is implemented and defaults to **1.0** as a last-resort safety net
- LLM summarization is implemented for OpenAI, Anthropic and Microsoft.Extensions.AI via `UseLlmSummarization(...)`
- summary checkpoint reuse and promotion are implemented inside the summarization strategy
- the default counter is `EstimatedTokenCounter`; custom counters and strategies have public builder routes, and provider
  input-token anchoring applies to every counter
- pinned messages survive all compaction stages
- DI registration via `AddConversationContext(...)` and factory-based creation is implemented
- OpenAI, Anthropic and Microsoft.Extensions.AI adapter helpers are available
- the Anthropic adapter is covered by unit tests and was run by hand against the Anthropic API in October 2026 on
  Claude Haiku 5.5, Sonnet 5.5, Opus 5.5 and Fable 5.1
- runtime recording flow is available through `SetSystemPrompt(...)`, `AddPinnedMessage(...)`, `AddUserMessage(...)`,
  `PrepareAsync(...)`, `RecordModelResponse(...)`, and `RecordToolResult(...)`

Known limits:

- one `ConversationContext` is for one caller at a time; overlapping calls are detected on a best-effort basis and throw
- token estimates leave out request overhead such as tool schemas until the first provider report
- the estimator runs 15% to 49% high on source code and non-Latin text
- one tool result larger than the budget cannot be fitted. The prepare returns `CompactionInsufficient` when it
  masked, summarized, or dropped other messages, and `CannotCompact` when it changed none; in both the request is over
  the budget and the caller decides whether to send it
- on a provider with a prompt cache the money saving differs from the token saving, because masking changes earlier
  messages
- the packages target `net10.0` only

What remains planned:

- broader multi-strategy pipeline expansion beyond current masking + summarization + emergency fallback
