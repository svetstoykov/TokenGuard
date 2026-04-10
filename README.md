# TokenGuard

TokenGuard is a .NET library for automatic context management in LLM agent loops.

It sits between your accumulated conversation history and the next model call, helping long-running tool-using loops
stay within budget without forcing you to hand-roll truncation logic.

- Drop-in for existing agent loops
- Provider-agnostic conversation model
- Sliding-window masking available today
- DI-friendly factory-based setup for application code

Domain background and longer-term architecture live in `assets/token-guard-spec.md`.

## Quick Start ⚡

The primary onboarding path is dependency injection.

Register `IConversationContextFactory` once at startup, then create a fresh context for each conversation. The factory
keeps configuration singleton-scoped while ensuring the stateful conversation object is not shared across requests.

### Default profile

Use the unnamed/default profile when the application has one main conversation budget.

```csharp
using Microsoft.Extensions.DependencyInjection;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Extensions;

var services = new ServiceCollection();

// Default 
services.AddConversationContext(builder => builder
    .WithMaxTokens(100_000)
    .WithCompactionThreshold(0.80));

// Use named profiles when different workloads need different token budgets or strategies.
services.AddConversationContext("analysis", builder => builder
    .WithMaxTokens(200_000)
    .WithCompactionThreshold(0.75));

using var serviceProvider = services.BuildServiceProvider();

using var conversationContext = serviceProvider
    .GetRequiredService<IConversationContextFactory>()
    .Create();
```

### Core loop

Call `PrepareAsync()` before each provider request. It decides whether the next outbound payload can use the full
recorded history or needs compaction. It does not rewrite `History` in place.

```csharp
using var conversationContext = factory.Create();

conversationContext.SetSystemPrompt("You are a precise coding assistant.");
conversationContext.AddUserMessage("Inspect the repository and summarize the failing tests.");

while (true)
{
    // Messages are compacted internally if necessary to keep context in bounds.
    var preparedMessages = await conversationContext.PrepareAsync(cancellationToken);

    // Send preparedMessages through your provider adapter here.
    var response = await chatClient.CompleteChatAsync(preparedMessages, chatOptions);

    conversationContext.RecordModelResponse(response.Segments, response.InputTokens);

    if (!response.RequestedToolCalls)
    {
        break;
    }

    foreach (var toolCall in response.ToolCalls)
    {
        var toolResult = _toolExecutor.Execute(toolCall);
        conversationContext.RecordToolResult(toolCall.Id, toolCall.Name, toolResult);
    }
}
```

### Non-DI path

If you are not using a container, you can construct `ConversationContext` directly with a `ContextBudget`, token
counter, and compaction strategy. Keep that as a secondary setup path; for most applications,
`AddConversationContext(...)` plus `IConversationContextFactory` is the intended entry point.

## What TokenGuard Does

TokenGuard manages the conversation state inside an LLM loop so application code can stay focused on provider calls and
tool execution.

- Tracks conversation growth across user, model, system, and tool messages
- Prepares the next outbound payload with `PrepareAsync()`
- Applies compaction when the configured threshold is reached
- Preserves system prompt handling as part of the managed flow
- Accepts provider-reported input token counts to improve later estimates

Current implementation focus:

- Sliding-window observation masking for tool-heavy conversations
- OpenAI-oriented adapter helpers in `src/TokenGuard.Extensions.OpenAI`
- Unit, integration, and live E2E coverage around compaction behavior
- Runnable console samples in `samples/TokenGuard.Samples.Console`

## Repository Layout

- `src/TokenGuard.Core` - core abstractions, message model, budget handling, and compaction pipeline
- `src/TokenGuard.Extensions.OpenAI` - OpenAI message conversion helpers and integration points
- `samples/TokenGuard.Samples.Console` - interactive sample app with OpenAI and Anthropic agent loops
- `tests/TokenGuard.Tests` - unit tests
- `tests/TokenGuard.IntegrationTests` - integration tests for cross-component behavior
- `tests/TokenGuard.E2E` - live end-to-end tests with real model loops and tool execution
- `tests/TokenGuard.TestCommon` - shared testing tools and fixtures

## Samples 🧪

After the quick-start snippets, the best full reference is `samples/TokenGuard.Samples.Console`.

The sample app includes:

- `Complete OpenAI loop`
- `Minimal OpenAI loop`
- `Minimal Anthropic loop`

Run it with:

```bash
dotnet run --project samples/TokenGuard.Samples.Console
```

The sample reads configuration from:

- environment variables
- `appsettings.json`
- `appsettings.{DOTNET_ENVIRONMENT}.json`

Preferred local setup is environment variables so secrets stay outside tracked files:

```bash
export OPENROUTER_API_KEY='your-openrouter-key'
export ANTHROPIC_API_KEY='your-anthropic-key'
dotnet run --project samples/TokenGuard.Samples.Console
```

The sample task input is read from `samples/TokenGuard.Samples.Console/Tasks/001-context-burn-task.txt`.

## Requirements

- .NET SDK 10.0 or newer
- An LLM provider API key for live samples or E2E tests
- macOS, Linux, or Windows shell capable of exporting environment variables

## Build 🔧

```bash
dotnet build
```

## Test ✅

Run the main automated suite:

```bash
dotnet test tests/TokenGuard.Tests
dotnet test tests/TokenGuard.IntegrationTests
```

Run the full test set when needed:

```bash
dotnet test
```

## Live E2E Tests 🌐

`tests/TokenGuard.E2E` contains live provider tests. These require real credentials and are intended for opt-in local
runs or CI jobs with injected secrets.

The OpenRouter E2E test reads `OPENROUTER_API_KEY` from the process environment.

### Local usage

If you have not already setup the env `OPENROUTER_API_KEY` you can run the OpenRouter E2E test by creating a local env file at `tests/TokenGuard.E2E/.env.local`:

```dotenv
OPENROUTER_API_KEY=your-openrouter-key
```

The project file already sets **Copy to Output Directory: Always** for `.env.local`, so the file is automatically
available to the test runner after build — no extra configuration needed.

Then run the test project normally:

```bash
dotnet test tests/TokenGuard.E2E/TokenGuard.E2E.csproj --filter FullyQualifiedName~OpenRouterAgentLoopE2ETests
```

You can append extra `dotnet test` arguments as needed:

```bash
dotnet test tests/TokenGuard.E2E/TokenGuard.E2E.csproj --filter FullyQualifiedName~OpenRouterAgentLoopE2ETests --logger "console;verbosity=detailed"
```

The test checks the process environment first and then falls back to `tests/TokenGuard.E2E/.env.local`, so GitHub
Actions can still inject `OPENROUTER_API_KEY` through job `env` or repository secrets without any test code changes.

## Configuration Notes

- OpenRouter sample and E2E flows use `OPENROUTER_API_KEY`
- Anthropic sample flow uses `ANTHROPIC_API_KEY`
- Do not commit real keys into `appsettings*.json`, scripts, or test files
- Keep local secret-bearing files in ignored paths only

## Current Status 🚧

The project currently centers on sliding-window masking.

What is current:

- sliding-window observation masking is implemented and usable now
- DI registration via `AddConversationContext(...)` and factory-based creation is implemented
- runtime recording flow is available through `SetSystemPrompt(...)`, `AddUserMessage(...)`, `PrepareAsync(...)`,
  `RecordModelResponse(...)`, and `RecordToolResult(...)`

What remains planned:

- summarization-based compaction
- tiered compaction
- dedicated compaction event callbacks and broader strategy pipeline expansion
