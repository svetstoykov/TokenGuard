# TokenGuard

**Keep agent loops sharp under pressure.** TokenGuard is a .NET library for automatic context management in LLM agent loops, with **provider-agnostic message modeling**, **drop-in DI setup**, **pinned-message durability**, **sliding-window masking**, and **emergency truncation safety rails** so long-running, tool-heavy conversations stay alive without hand-rolled context pruning.

- Drop-in for existing agent loops
- Provider-agnostic conversation model
- Two-tiered compaction: masking first, truncation only in emergency
- Pinned messages that survive compaction unchanged
- DI-friendly factory-based setup for application code
- OpenAI and Anthropic adapter helpers

Domain background and longer-term architecture live in `assets/token-guard-spec.md`.

## Quick Start ⚡

Primary onboarding path is dependency injection.

Register `IConversationContextFactory` once at startup, then create a fresh context for each conversation. Factory keeps configuration singleton-scoped while ensuring stateful conversation object is not shared across requests.

### Default profile

Use unnamed/default profile when application has one main conversation budget.

```csharp
using Microsoft.Extensions.DependencyInjection;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Extensions;

var services = new ServiceCollection();

services.AddConversationContext(builder => builder
    .WithMaxTokens(100_000)
    .WithCompactionThreshold(0.80)
    .WithEmergencyThreshold(0.95));

services.AddConversationContext("analysis", builder => builder
    .WithMaxTokens(200_000)
    .WithCompactionThreshold(0.75)
    .WithEmergencyThreshold(0.90));

using var serviceProvider = services.BuildServiceProvider();

using var conversationContext = serviceProvider
    .GetRequiredService<IConversationContextFactory>()
    .Create();
```

### Core loop

Call `PrepareAsync()` before each provider request. It decides whether next outbound payload can use full recorded history, apply masking-based compaction, or fall back to emergency truncation. It does not rewrite `History` in place.

```csharp
using TokenGuard.Extensions.OpenAI;

using var conversationContext = factory.Create();

conversationContext.SetSystemPrompt("You are a precise coding assistant.");
conversationContext.AddPinnedMessage(MessageRole.User, "Repository root is /workspace/project. Keep all paths relative to it.");
conversationContext.AddUserMessage("Inspect repository and summarize failing tests.");

while (true)
{
    var preparedMessages = await conversationContext.PrepareAsync(cancellationToken);
    var providerMessages = preparedMessages.ForOpenAI();

    var response = await chatClient.CompleteChatAsync(providerMessages, chatOptions, cancellationToken);

    conversationContext.RecordModelResponse(response.ResponseSegments(), response.InputTokens());

    if (response.ToolCalls.Count == 0)
    {
        break;
    }

    foreach (var toolCall in response.ToolCalls)
    {
        var toolResult = _toolExecutor.Execute(toolCall);
        conversationContext.RecordToolResult(toolCall.Id, toolCall.FunctionName, toolResult);
    }
}
```

### Non-DI path

If you are not using container, you can construct `ConversationContext` directly with `ContextBudget`, token counter, and compaction strategy. Keep that as secondary setup path; for most applications, `AddConversationContext(...)` plus `IConversationContextFactory` is intended entry point.

## What TokenGuard Does

TokenGuard manages conversation state inside an LLM loop so application code stays focused on provider calls and tool execution.

- Tracks conversation growth across user, model, system, pinned, and tool messages
- Prepares next outbound payload with `PrepareAsync()`
- Applies masking when configured compaction threshold is reached
- Falls back to oldest-first emergency truncation when prepared payload still exceeds emergency threshold
- Preserves pinned messages at their original positions during both masking and truncation
- Accepts provider-reported input token counts to improve later estimates
- Emits compaction observer events for monitoring and diagnostics

## Compaction Model

TokenGuard now uses a practical two-tier compaction flow designed for tool-heavy agent loops:

1. **Tier 1 - Observation masking**: configured compaction strategy shrinks history by masking older tool results while keeping recent turns intact.
2. **Tier 2 - Emergency truncation**: if masked payload still exceeds emergency threshold, TokenGuard drops oldest eligible unpinned messages first while preserving pinned messages and newest active tail.

This gives normal runs a cheap, structure-preserving compaction path and keeps an emergency escape hatch for oversized conversations that still do not fit after masking.

### Pinned messages

Pinned messages are durable conversation entries recorded with `AddPinnedMessage(...)` or implicitly through `SetSystemPrompt(...)`.

- Never masked
- Never truncated by emergency fallback
- Reinserted at original positions after compaction
- Counted against reserved budget during preparation

Use pinned messages for durable task constraints, repository rules, user preferences, or other high-value instructions that must survive long sessions.

## Provider Adapters

TokenGuard stays provider-agnostic in core, then offers adapter helpers for common SDKs.

- `src/TokenGuard.Extensions.OpenAI` - convert prepared messages with `ForOpenAI()` and map provider responses back with `ResponseSegments()` and `InputTokens()`
- `src/TokenGuard.Extensions.Anthropic` - convert prepared messages with `ForAnthropic()` and map provider responses back with `ResponseSegments()` and `InputTokens()`

## Repository Layout

- `src/TokenGuard.Core` - core abstractions, message model, budget handling, pinned-message support, observer hooks, and compaction pipeline
- `src/TokenGuard.Extensions.OpenAI` - OpenAI message conversion helpers and integration points
- `src/TokenGuard.Extensions.Anthropic` - Anthropic message conversion helpers and integration points
- `samples/TokenGuard.Samples.Console` - interactive sample app with OpenAI and Anthropic agent loops
- `tests/TokenGuard.Tests` - unit tests
- `tests/TokenGuard.IntegrationTests` - integration tests for cross-component behavior
- `tests/TokenGuard.E2E` - live end-to-end tests with real model loops and tool execution
- `tests/TokenGuard.TestCommon` - shared testing tools and fixtures

## Samples 🧪

After quick-start snippets, best full reference is `samples/TokenGuard.Samples.Console`.

Sample app includes:

- `Complete OpenAI loop`
- `Minimal OpenAI loop`
- `Minimal Anthropic loop`

Run it with:

```bash
dotnet run --project samples/TokenGuard.Samples.Console
```

Sample reads configuration from:

- environment variables
- `appsettings.json`
- `appsettings.{DOTNET_ENVIRONMENT}.json`

Preferred local setup is environment variables so secrets stay outside tracked files:

```bash
export OPENROUTER_API_KEY='your-openrouter-key'
export ANTHROPIC_API_KEY='your-anthropic-key'
dotnet run --project samples/TokenGuard.Samples.Console
```

Sample task input is read from `samples/TokenGuard.Samples.Console/Tasks/001-context-burn-task.txt`.

## Requirements

- .NET SDK 10.0 or newer
- LLM provider API key for live samples or E2E tests
- macOS, Linux, or Windows shell capable of exporting environment variables

## Build 🔧

```bash
dotnet build
```

## Test ✅

Run main automated suite:

```bash
dotnet test tests/TokenGuard.Tests
dotnet test tests/TokenGuard.IntegrationTests
```

Run full test set when needed:

```bash
dotnet test
```

## Live E2E Tests 🌐

`tests/TokenGuard.E2E` contains live provider tests. These require real credentials and are intended for opt-in local runs or CI jobs with injected secrets.

OpenRouter E2E test reads `OPENROUTER_API_KEY` from process environment.

### Local usage

If you have not already set `OPENROUTER_API_KEY`, create local env file at `tests/TokenGuard.E2E/.env.local`:

```dotenv
OPENROUTER_API_KEY=your-openrouter-key
```

Project already sets **Copy to Output Directory: Always** for `.env.local`, so file is automatically available to test runner after build.

Then run test project normally:

```bash
dotnet test tests/TokenGuard.E2E/TokenGuard.E2E.csproj --filter FullyQualifiedName~OpenRouterAgentLoopE2ETests
```

You can append extra `dotnet test` arguments as needed:

```bash
dotnet test tests/TokenGuard.E2E/TokenGuard.E2E.csproj --filter FullyQualifiedName~OpenRouterAgentLoopE2ETests --logger "console;verbosity=detailed"
```

Test checks process environment first and then falls back to `tests/TokenGuard.E2E/.env.local`, so GitHub Actions can still inject `OPENROUTER_API_KEY` through job `env` or repository secrets without any test code changes.

## Configuration Notes

- OpenRouter sample and E2E flows use `OPENROUTER_API_KEY`
- Anthropic sample flow uses `ANTHROPIC_API_KEY`
- Do not commit real keys into `appsettings*.json`, scripts, or test files
- Keep local secret-bearing files in ignored paths only

## Current Status 🚧

What is current:

- sliding-window observation masking is implemented and usable now
- two-tier behavior is implemented: masking under normal pressure, truncation under emergency pressure
- pinned messages are implemented and survive both compaction tiers
- DI registration via `AddConversationContext(...)` and factory-based creation is implemented
- OpenAI and Anthropic adapter helpers are available
- runtime recording flow is available through `SetSystemPrompt(...)`, `AddPinnedMessage(...)`, `AddUserMessage(...)`, `PrepareAsync(...)`, `RecordModelResponse(...)`, and `RecordToolResult(...)`
- compaction observer callbacks are available through `ICompactionObserver`

What remains planned:

- summarization-based compaction
- broader multi-strategy pipeline expansion beyond current masking + emergency fallback
