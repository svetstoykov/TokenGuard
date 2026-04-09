# SemanticFold

**A .NET library for automatic context management in LLM agent loops.**

NuGet Package · MIT License · Targets .NET 8+

---

## The Problem

Every developer building an LLM-powered agent in .NET hits the same wall. The standard agent loop looks like this:

```
while (not done)
{
    Send conversation to LLM →
    LLM requests tool calls →
    Execute tool calls →
    Append tool results to conversation →
    Send updated conversation back to LLM →
    Repeat
}
```

Each iteration appends more messages, tool calls, and tool results to the conversation history. The context grows with every turn — not linearly, but compoundingly, because each request must include the entire conversation history that came before it. Within 10-20 loops of a moderately complex task, you're looking at tens of thousands of tokens of context, most of which is stale tool output the model no longer needs.

The consequences are predictable and painful:

- **Token costs explode.** You pay per token on every request, and each request includes everything from all previous turns.
- **Model performance degrades.** Research consistently shows that as context grows, LLMs struggle to make effective use of the information. The useful signal gets buried in noise. The model loses focus on what matters.
- **Context window limits get hit.** Even with 200K token windows, a busy agent loop with file reads, search results, and test outputs will fill the window within a single work session. At that point, the agent either crashes or you truncate blindly and lose critical state.

Today, if you're building agents with the Anthropic, OpenAI, or Azure OpenAI SDKs in .NET, you have two options: manage this yourself (fragile, error-prone, and different for every project), or don't manage it at all and hope for the best. There is no standard, reusable library for this in the .NET ecosystem.

SemanticFold fills that gap.

---

## What SemanticFold Does

SemanticFold sits inside your agent loop and manages the conversation context automatically. It monitors token usage, decides when compaction is needed, and applies the appropriate strategy — all behind a clean interface that requires minimal changes to your existing code.

You keep writing your agent loop. SemanticFold keeps the context healthy.

---

## Target Users

- **.NET developers building LLM agents** using the Anthropic SDK, Azure OpenAI SDK, OpenAI SDK, or Semantic Kernel.
- **Teams building internal tools and automations** where agents perform multi-step tasks (code generation, data analysis, document processing, customer service workflows).
- **Developers who have already hit context limits** in production and are managing them with ad-hoc truncation or manual summarization.

---

## Design Principles

1. **Drop-in, not rip-and-replace.** SemanticFold should be adoptable in an afternoon. It wraps your existing message list — it doesn't replace your LLM client, your tool execution, or your agent orchestration.

2. **Strategy as configuration, not code.** Switching from simple truncation to LLM-powered summarization should be a one-line configuration change, not a rewrite.

3. **Sensible defaults, full control.** Out of the box, SemanticFold should work well without tuning. But every threshold, window size, and strategy parameter should be overridable for teams that need precision.

4. **Provider-agnostic.** SemanticFold works with any LLM provider. It operates on a standard message abstraction — it doesn't care whether the underlying model is Claude, GPT, Gemini, or a local model.

---

## Context Management Strategies

Based on current research (JetBrains Research, "Cutting Through the Noise," NeurIPS 2025; Claude Code's three-tier compaction system; OpenHands; SWE-agent), SemanticFold is currently centered on a sliding-window masking implementation and is planned to support three core strategies, plus a hybrid approach, as the library expands.

### Strategy 1: Sliding Window (Observation Masking)

The simplest and most cost-effective approach. Old tool results and observations are replaced with short placeholders while the agent's reasoning and action history is preserved in full.

**How it works:**
- A configurable window of the N most recent turns is kept in full.
- Turns outside the window have their tool results / observations replaced with a placeholder (e.g., `[Tool result cleared — {tool_name}, {timestamp}]`).
- The agent's own reasoning and actions remain intact across all turns.

**Implementation status:**
- Implemented today as the library's active compaction strategy.
- Exposed via `SlidingWindowStrategy` with `SlidingWindowOptions` for window size, protected-window token fraction, and placeholder formatting.
- Current behavior masks older `ToolResultContent` blocks and keeps recent messages unchanged; it does not yet persist a compacted history back into the conversation state.

**When to use it:**
- Default choice for current workloads.
- Agents where tool outputs are large but the agent only needs them for immediate reasoning (file reads, search results, test output, API responses).
- Cost-sensitive deployments.

**Trade-offs:**
- Cannot handle infinite turns — context still grows, just much more slowly.
- No semantic compression — if an early observation contains a critical insight, it's gone once it leaves the window.

**Research basis:** SWE-agent's rolling window approach. JetBrains research found that observation masking matched or outperformed LLM summarization in 4 out of 5 test settings while being significantly cheaper. The key insight is that agent trajectories heavily skew toward observation data, so masking observations alone captures most of the savings.

### Strategy 2: LLM Summarization

A secondary LLM call compresses the conversation history into a structured summary. The full history is replaced with this summary, and the agent continues from the condensed state.

**How it works:**
- When context exceeds a threshold, SemanticFold sends the conversation to an LLM with a summarization prompt.
- The prompt is structured to preserve: completed work, current state, in-progress tasks, key decisions, file/resource references, and next steps.
- The summary replaces all prior messages. The N most recent turns are kept in full alongside the summary.
- Custom summarization prompts can be provided for domain-specific needs.

**When to use it:**
- Very long-running tasks (50+ turns) where observation masking alone would still exceed the context window.
- Tasks where early context contains decisions and reasoning that must be preserved semantically, not just structurally.

**Trade-offs:**
- Additional LLM API call per compaction event = additional cost and latency.
- Research shows LLM summarization can cause "trajectory elongation" — the agent runs ~15% more turns because the summary smooths over signals that the agent should stop. This increases overall cost.
- Summary quality depends on the summarization model and prompt.

**Research basis:** OpenHands' condensation approach. Claude Code's Tier 3 compaction. Anthropic's server-side compaction API (`compact_20260112`).

### Strategy 3: Tiered Compaction

Inspired directly by Claude Code's three-tier system, this approach applies progressively heavier compaction as context pressure increases.

**How it works:**
- **Tier 1 — Micro-compaction (runs every turn):** Lightweight cleanup. Old tool results beyond the most recent N are cleared and replaced with placeholders. No LLM call involved. Fast and cheap.
- **Tier 2 — Observation Masking (runs at medium pressure):** When context crosses a configurable threshold (e.g., 60% of window), apply full observation masking with a rolling window.
- **Tier 3 — LLM Summarization (runs at high pressure):** When context crosses a higher threshold (e.g., 85% of window), trigger a full summarization. The summary replaces the conversation history. Recent turns and critical state are preserved.

**When to use it:**
- Production agents that need to balance cost, performance, and reliability.
- Long-running autonomous agents where you want maximum efficiency with a safety net.

**Trade-offs:**
- More configuration parameters (three thresholds, window sizes per tier).
- Slightly more complex to reason about — but the defaults should handle 90% of cases.

**Research basis:** Claude Code's production compaction engine (microcompaction → server-side clearing → full summarization). JetBrains Research's hybrid approach which achieved 7% cost reduction over pure masking and 11% over pure summarization.

### Strategy 4: Hybrid (Recommended Default)

The approach recommended by JetBrains Research. Uses observation masking as the primary mechanism and falls back to LLM summarization only when context is under serious pressure.

**How it works:**
- Observation masking runs continuously with a configurable window.
- LLM summarization triggers only after a large batch of turns accumulates AND context crosses a high threshold.
- Tuned to minimize LLM summarization calls while preventing context overflow.

**When to use it:**
- The recommended default for most users.
- Achieves the best balance of cost, performance, and reliability according to current research.

---

## Core Concepts

### The Message Abstraction

SemanticFold needs a standard representation of a conversation turn that is provider-agnostic. This abstraction should capture:

- **Role**: user, assistant, tool_result, system
- **Content**: text content, structured content, tool call definitions
- **Metadata**: timestamp, token count (estimated or actual), tool name, turn index
- **Compaction state**: full, masked, summarized

Adapters will map between this abstraction and provider-specific message formats (Anthropic `MessageParam`, OpenAI `ChatCompletionMessage`, etc.).

Current implementation notes:

- The core abstraction is implemented as `Message`, with roles `System`, `User`, `Model`, and `Tool`.
- Message content is represented as `ContentSegment` values, currently `TextContent`, `ToolUseContent`, and `ToolResultContent`.
- Implemented metadata includes `Timestamp`, cached `TokenCount`, and `CompactionState`.
- A dedicated turn index is not currently stored on the message model.

### The Context Budget

A `ContextBudget` defines the constraints SemanticFold operates within:

- **Max tokens**: the hard ceiling (context window size minus reserved output tokens minus system prompt size).
- **Compaction threshold**: the percentage of max tokens at which compaction triggers (default: 80%).
- **Emergency threshold**: the percentage at which aggressive compaction fires regardless of strategy (default: 95%).
- **Reserved tokens**: tokens reserved for the system prompt, tools definitions, and other fixed content that doesn't change between turns.

Current implementation notes:

- `ContextBudget` is implemented with `MaxTokens`, `CompactionThreshold`, `EmergencyThreshold`, and `ReservedTokens`.
- It also exposes computed `AvailableTokens`, `CompactionTriggerTokens`, and `EmergencyTriggerTokens`.
- `ConversationContext.Prepare()` currently triggers compaction at the normal compaction threshold and adjusts reserved tokens to account for preserved system messages.
- The emergency threshold is modeled in the budget but is not yet used by the current strategy pipeline.

### The Compaction Event

When SemanticFold compacts, it produces a `CompactionEvent` that includes:

- **Strategy used**: which strategy triggered.
- **Tokens before**: context size before compaction.
- **Tokens after**: context size after compaction.
- **Turns affected**: which turns were modified.
- **Summary content** (if LLM summarization was used).

This allows logging, monitoring, and debugging of compaction behavior in production.

Implementation status:

- A dedicated `CompactionEvent` type and callback pipeline are not implemented yet.
- Current samples detect compaction by inspecting prepared message states (`Original`, `Masked`, `Summarized`) after `Prepare()` returns.

---

## Integration Points

### Where SemanticFold Lives in the Loop

```csharp
while (!done)
{
    var preparedMessages = conversationContext.Prepare();
    var providerMessages = preparedMessages.ForOpenAI();

    var response = await chatClient.CompleteChatAsync(providerMessages, chatOptions);

    conversationContext.RecordModelResponse(response.ResponseSegments(), response.InputTokens());

    if (response.FinishReason == ChatFinishReason.ToolCalls)
    {
        foreach (var call in response.ToolCalls)
        {
            var result = ExecuteTool(call);
            conversationContext.RecordToolResult(call.Id, call.FunctionName, result);
        }
    }
}
```

Current touch points:

1. **`Prepare()`** — implemented. Called before sending to the LLM. Evaluates context size, applies compaction if needed, and returns the managed message list.
2. **Recording APIs** — implemented as `SetSystemPrompt()`, `AddUserMessage()`, `RecordModelResponse()`, and `RecordToolResult()` to append typed history entries and cache token estimates.
3. **`Observe()`** — not currently implemented as a separate API. Its intended responsibilities are covered today by the recording methods above.

### Provider Adapters

SemanticFold currently ships with:

- **OpenAI / OpenAI-compatible chat adapter** — implemented as extension methods that map prepared `Message` values to `OpenAI.Chat.ChatMessage` instances and extract `TextContent`, `ToolUseContent`, and provider input-token usage from `ChatCompletion` responses.

Planned adapters remain in scope:

- **Anthropic SDK for .NET** — maps to/from `MessageParam`, handles content segments, tool use segments, and tool result segments.
- **Azure OpenAI / OpenAI SDK** — broader provider coverage beyond the current OpenAI chat adapter surface.
- **Semantic Kernel** — integrates with SK's `ChatHistory` abstraction.
- **Raw/Custom** — a generic adapter for any provider, using SemanticFold's own message type.

### Token Counting

Accurate token counting is critical for compaction decisions. SemanticFold supports:

- **Estimated counting** (default) — uses a characters-to-tokens heuristic. Fast, no API call, ~90% accurate. Good enough for most use cases.
- **Provider counting** — calls the provider's token counting API (where available). Accurate but adds latency.
- **Custom counting** — plug in your own `ITokenCounter` implementation.

Current implementation notes:

- `EstimatedTokenCounter` is implemented and used by default.
- Custom counting is supported through the `ITokenCounter` interface.
- Provider-side token counting is not implemented as a dedicated counter yet, but `ConversationContext.RecordModelResponse(..., providerInputTokens)` can consume provider-reported input token counts to anchor and correct later estimates.

---

## Configuration

```csharp
var conversationContext = new ConversationContextBuilder()
    .WithMaxTokens(200_000)
    .WithCompactionThreshold(0.80)
    .WithEmergencyThreshold(0.95)
    .WithReservedTokens(2_000)
    .WithTokenCounter(new EstimatedTokenCounter())
    .WithStrategy(new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 10)))
    .Build();
```

Minimal configuration for quick start:

```csharp
var conversationContext = ConversationContextBuilder.Default(maxTokens: 200_000);
```

Today the builder configures the core context, budget, token counter, and compaction strategy. Summarization-specific configuration, adapter registration, and compaction callbacks are planned but not implemented yet.

---

## Summarization Prompt Contract

When LLM summarization triggers, the prompt sent to the summarization model must produce a structured, reconstruction-grade summary. Based on research from Claude Code's compaction system and the JetBrains hybrid approach, the default prompt requests:

1. **Completed work** — what tasks have been finished.
2. **Current state** — files, resources, and data currently in play. Their status.
3. **In-progress work** — what is actively being worked on right now.
4. **Key decisions** — architectural choices, user preferences, constraints established during the conversation.
5. **Critical data** — specific values, variable names, error messages, or configuration details that would be expensive to re-derive.
6. **Next steps** — clear actions the agent should take to continue.

The summary is wrapped in a structured format (XML or JSON) so SemanticFold can parse, store, and re-inject it reliably.

Users can provide a fully custom summarization prompt for domain-specific needs (e.g., "Focus on preserving SQL queries and database schema details" for a data engineering agent).

---

## Rehydration (Post-Compaction Recovery)

After compaction, SemanticFold supports an optional rehydration step inspired by Claude Code's post-compaction reconstruction. This is particularly valuable for the Tiered and Hybrid strategies:

- **Summary injection** — the compacted summary is wrapped in a continuation message that tells the agent to resume from the summary without asking the user to repeat information.
- **Resource re-injection** — if the agent was working with specific files or data, SemanticFold can re-read and re-inject the most recent resources (configurable, capped at a token budget).
- **Continuation instruction** — a standard message appended after the summary: "This session continues from a previous context that was compacted. The summary above covers prior work. Continue from where the conversation left off without asking the user to repeat information."

---

## What SemanticFold Is Not

- **Not an agent framework.** It doesn't manage tool execution, planning, or orchestration. It manages context. Use it alongside Semantic Kernel, AutoGen, or your own agent loop.
- **Not an LLM client.** It doesn't send requests to LLMs (except for the optional summarization call). It manages the message list you pass to your own client.
- **Not a prompt engineering tool.** It doesn't optimize your system prompts or tool definitions. It manages the conversation history that sits alongside them.

---

## Project Roadmap

### Phase 1: Core Library (v0.1)

- Message abstraction and provider adapters (Anthropic, OpenAI/Azure OpenAI).
- Sliding Window strategy (observation masking).
- Estimated token counting.
- `Prepare()` and `Observe()` integration points.
- NuGet package published.
- README with examples, quick start guide, and API reference.

### Phase 2: Summarization (v0.2)

- LLM Summarization strategy.
- Default summarization prompt contract.
- Custom summarization prompt support.
- Compaction event logging and callbacks.
- Rehydration / post-compaction recovery.

### Phase 3: Advanced Strategies (v0.3)

- Tiered Compaction strategy.
- Hybrid strategy (recommended default).
- Configurable thresholds and window sizes per strategy.
- Semantic Kernel adapter.
- Provider-based token counting.

### Phase 4: Production Hardening (v1.0)

- Benchmarks against raw agent baseline (token savings, cost reduction, task completion rates).
- Edge case handling (compaction during pending tool calls, concurrent access).
- Comprehensive test suite.
- Documentation site.
- Community feedback and API stabilization.

---

## Competitive Landscape

| Solution | Language | Approach | Limitation |
|----------|----------|----------|------------|
| Anthropic Server-Side Compaction | API-level | LLM summarization via API | Anthropic-only, no observation masking, no hybrid |
| OpenAI SDK | Python | No built-in context management | Manual implementation required |
| Semantic Kernel | .NET | Token budget via `ChatHistory` | Basic truncation only, no strategies |
| LangChain | Python | `ConversationSummaryMemory` | Python-only, tightly coupled to LangChain |
| Claude Code | TypeScript/Rust | 3-tier compaction | Not a library — embedded in Claude Code, not reusable |
| **SemanticFold** | **.NET** | **Multiple strategies, provider-agnostic** | **New, unproven** |

The .NET ecosystem has no dedicated, provider-agnostic context management library. SemanticFold is the first.

---

## Success Metrics

For the library to be considered successful:

- **Functional**: Demonstrably reduces token usage by 40-60% in a standard agent benchmark (consistent with JetBrains research findings).
- **Adoptable**: A developer can integrate SemanticFold into an existing agent loop in under 30 minutes.
- **Reliable**: Zero data loss in the most recent N turns (sliding window guarantee). Summarization preserves all six categories of the prompt contract.
- **Discoverable**: First-page NuGet search results for "context management," "LLM context," "agent context," and "token management."

---

## References

- Lindenbauer et al., "Cutting Through the Noise: Smarter Context Management for LLM-Powered Agents," JetBrains Research / TUM, NeurIPS 2025. [Paper](https://arxiv.org/pdf/2508.21433) · [Blog](https://blog.jetbrains.com/research/2025/12/efficient-context-management/) · [Code](https://github.com/JetBrains-Research/the-complexity-trap)
- Claude Code Compaction System — three-tier architecture (microcompaction, auto-compaction, full summarization). [Deep Dive](https://decodeclaude.com/compaction-deep-dive/)
- Anthropic Server-Side Compaction API — `compact_20260112` beta. [Docs](https://platform.claude.com/docs/en/build-with-claude/compaction)
- Anthropic Context Editing API — tool result clearing and thinking block management. [Docs](https://platform.claude.com/docs/en/build-with-claude/context-editing)
- Liu et al., "Lost in the Middle: How Language Models Use Long Contexts," 2023. [Paper](https://arxiv.org/abs/2307.03172)
- OpenHands Context Condensation. [Blog](https://openhands.dev/blog/openhands-context-condensensation-for-more-efficient-ai-agents)
- SWE-agent observation masking via rolling window. [Paper](https://arxiv.org/abs/2405.15793)
