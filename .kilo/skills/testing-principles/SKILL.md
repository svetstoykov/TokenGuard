---
name: testing-principles
description: >
  Apply best practices for writing unit, integration, and end-to-end (E2E) tests.
  Use this skill whenever the user asks for help writing, reviewing, or structuring
  tests of any kind — including xUnit, NUnit, MSTest, Vitest, or any other framework.
  Trigger on phrases like "write a test", "add unit tests", "review my tests",
  "how should I test this", "help with integration tests", "E2E test setup", or any
  request to improve test coverage or test quality. E2E in this context means running
  a real agent loop against a live LLM API (OpenRouter, Anthropic, etc.), not browser
  automation. Also trigger when the user shares existing test code that smells wrong
  or is hard to maintain — even if they don't explicitly ask for a review.
---

# Testing Principles

Canonical shared guidance also lives at `ai/skills/testing-principles.md`. Keep this Kilo skill aligned with that file so non-Kilo agents can follow the same behavior.

A decision-making and code-generation guide for writing high-quality unit, integration,
and E2E tests. These five principles apply across all frameworks and languages but
examples are given in C#/.NET (xUnit + FluentAssertions) to reflect the primary usage
context. E2E in this context means running a real agent loop against a live LLM API
— not browser automation.

---

## The Five Core Principles

### 1. Arrange-Act-Assert (AAA)

Every test has exactly three phases, clearly separated:

- **Arrange** — set up the system under test (SUT), its dependencies, and input data
- **Act** — invoke the single operation being tested
- **Assert** — verify the observable outcome

```csharp
// C# / xUnit
[Fact]
public async Task PlaceOrder_WhenInventorySufficient_ReturnsConfirmedOrder()
{
    // Arrange
    var inventory = new FakeInventory(stock: 10);
    var service = new OrderService(inventory);
    var request = new OrderRequest(productId: "SKU-1", quantity: 3);

    // Act
    var result = await service.PlaceOrderAsync(request);

    // Assert
    result.Status.Should().Be(OrderStatus.Confirmed);
}
```

**Smell to watch for:** Setup and assertion mixed together, or assertions spread across
multiple logical phases. If you can't identify the three phases at a glance, split the test.

---

### 2. One Behavior Per Test

Each test should have a single reason to fail. Name the test after the *behavior*, not
the method:

```
// Bad
CreateOrder_Test()

// Good
CreateOrder_WhenInventoryInsufficient_Returns422()
CreateOrder_WhenProductDiscontinued_ThrowsProductUnavailableException()
CreateOrder_WhenQuantityIsZero_ReturnsBadRequest()
```

Multiple `Assert` calls are fine if they all verify facets of the same behavior
(e.g., checking both `StatusCode` and `ErrorMessage` on a 422 response).
What's not fine is testing two *distinct behaviors* in one test body.

---

### 3. Test Behavior, Not Implementation

Assert on observable outcomes — return values, state changes, HTTP responses, UI
changes — not on internal mechanics.

```csharp
// Bad: tests implementation detail (how many times a method was called)
mockRepo.Verify(r => r.SaveAsync(It.IsAny<Order>()), Times.Once);

// Good: tests the observable outcome
var saved = await db.Orders.FindAsync(result.OrderId);
saved.Should().NotBeNull();
saved!.Status.Should().Be(OrderStatus.Pending);
```

Corollary: prefer fakes and stubs over mocks where possible. Mocks encode
implementation assumptions; fakes encode behavioral contracts.

---

### 4. Isolation and Determinism

| Layer | Isolation strategy |
|---|---|
| **Unit** | No I/O. Inject all dependencies. Use fakes/stubs for time, randomness, and LLM calls. |
| **Integration** | Own your state. Swap real LLM clients for deterministic fakes at the boundary. Each test constructs its own conversation from scratch. |
| **E2E** | Real LLM API calls (OpenRouter, Anthropic, etc.). Gate behind a `[Trait]` category so they never run in standard CI. Each test constructs its own conversation from scratch — no shared state. Assert structurally, not on exact text. |

```csharp
// Controlling time in .NET
public class FakeClock : ISystemClock
{
    public DateTimeOffset UtcNow { get; set; } = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
}
```

A flaky test — one that passes sometimes and fails other times — is worse than no test.
It erodes trust in the entire suite. Treat flakiness as a P1 bug.

---

### 5. Meaningful Failure Messages

When a test fails in CI at 2am, the failure output is the only context available.

- Use a fluent assertion library that produces readable diffs (`FluentAssertions` in .NET,
  `expect` from Vitest/Jest with `.toMatchObject`)
- Name test cases using the pattern: `Subject_Scenario_ExpectedOutcome`
- Mirror your test project structure to your source project so the failing test path
  maps directly to the code under test
- Add a `because` argument on critical assertions when the failure reason isn't obvious:

```csharp
result.Items.Should().HaveCount(3, because: "three line items were submitted in the request");
```

---

## Decision Guide: Which Layer to Use

| Question | Unit | Integration | E2E |
|---|---|---|---|
| Pure logic / algorithm? | ✅ | — | — |
| Token counting / threshold math? | ✅ | — | — |
| Strategy selection logic? | ✅ | — | — |
| Compaction pipeline with a fake LLM client? | — | ✅ | — |
| Message list wiring / orchestration? | — | ✅ | — |
| Does compaction fire at the right token threshold against a real model? | — | — | ✅ |
| Does the agent loop remain coherent after summarization with a real model? | — | — | ✅ |
| Does the library work end-to-end with a real OpenRouter/Anthropic key? | — | — | ✅ |

Aim for the [Testing Trophy](https://kentcdodds.com/blog/the-testing-trophy-and-testing-classifications):
the bulk of tests should be integration tests, with a smaller pyramid of unit tests
for pure logic and a lean set of E2E tests for critical paths.

---

## Test Data Builders

When Arrange blocks grow beyond 5–6 lines, extract a builder that constructs the
object graph with sensible defaults and lets individual tests override only the values
they care about. This is especially important for `ConversationContext` and message
lists, which are the primary inputs across most TokenGuard tests.

```csharp
public class ConversationBuilder
{
    private int _maxTokens = 10_000;
    private double _compactionThreshold = 0.80;
    private ITokenCounter _counter = new EstimatedTokenCounter();
    private ICompactionStrategy _strategy = new SlidingWindowStrategy(
        new SlidingWindowOptions(windowSize: 10));
    private readonly List<Action<ConversationContext>> _messages = [];

    public ConversationBuilder WithMaxTokens(int max) { _maxTokens = max; return this; }
    public ConversationBuilder WithThreshold(double t) { _compactionThreshold = t; return this; }
    public ConversationBuilder WithCounter(ITokenCounter c) { _counter = c; return this; }
    public ConversationBuilder WithStrategy(ICompactionStrategy s) { _strategy = s; return this; }

    public ConversationBuilder WithUserMessage(string text)
    {
        _messages.Add(ctx => ctx.AddUserMessage(text));
        return this;
    }

    public ConversationBuilder WithToolResult(string callId, string name, string result)
    {
        _messages.Add(ctx => ctx.RecordToolResult(callId, name, result));
        return this;
    }

    public ConversationContext Build()
    {
        var ctx = new ConversationContextBuilder()
            .WithMaxTokens(_maxTokens)
            .WithCompactionThreshold(_compactionThreshold)
            .WithTokenCounter(_counter)
            .WithStrategy(_strategy)
            .Build();

        foreach (var addMessage in _messages)
            addMessage(ctx);

        return ctx;
    }
}
```

Usage in a test:

```csharp
[Fact]
public void Prepare_WhenOverThreshold_CompactsOldToolResults()
{
    // Arrange — only specify what this test cares about
    var ctx = new ConversationBuilder()
        .WithMaxTokens(500)
        .WithThreshold(0.60)
        .WithUserMessage("Read file X")
        .WithToolResult("call-1", "read_file", new string('x', 400))
        .WithUserMessage("Now fix the bug")
        .Build();

    // Act
    var prepared = ctx.Prepare();

    // Assert
    prepared.Should().Contain(m =>
        m.CompactionState == CompactionState.Masked,
        because: "the old tool result should be masked after threshold exceeded");
}
```

Adapt the builder as the public API evolves. The pattern stays the same: defaults for
everything, fluent overrides for what matters per test, and a `Build()` that returns
the SUT ready to act on.

---

## Specification Tests (Cross-Implementation Contract Testing)

When multiple classes implement the same interface — like `ICompactionStrategy` — define
the behavioral contract once as an abstract test base. Each implementation inherits the
base and plugs in its own factory. This ensures every strategy passes the same contract
tests and prevents drift between implementations.

This is the pattern the EF Core repo uses for its `Specification.Tests` project: abstract
base classes define what a correct provider must do, and each provider (SQL Server,
SQLite, Cosmos) inherits them with its own fixture.

```csharp
public abstract class CompactionStrategyContractTests
{
    protected abstract ICompactionStrategy CreateStrategy();

    [Fact]
    public void Compact_WhenNoMessagesExceedBudget_ReturnsAllOriginal()
    {
        // Arrange
        var strategy = CreateStrategy();
        var messages = new ConversationBuilder()
            .WithMaxTokens(10_000)
            .WithUserMessage("Hello")
            .Build();

        // Act
        var result = strategy.Compact(messages.Messages, budget: 10_000);

        // Assert
        result.Should().OnlyContain(m => m.CompactionState == CompactionState.Original);
    }

    [Fact]
    public void Compact_WhenBudgetExceeded_ReducesTotalTokenCount()
    {
        var strategy = CreateStrategy();
        var ctx = new ConversationBuilder()
            .WithMaxTokens(500)
            .WithUserMessage("First message")
            .WithToolResult("c1", "tool", new string('x', 400))
            .WithUserMessage("Second message")
            .WithToolResult("c2", "tool", new string('y', 400))
            .Build();
        var before = ctx.TokenCount;

        var result = strategy.Compact(ctx.Messages, budget: 500);

        result.Sum(m => m.TokenCount).Should().BeLessThan(before,
            because: "compaction must reduce total token usage");
    }

    [Fact]
    public void Compact_NeverDropsSystemMessage()
    {
        var strategy = CreateStrategy();
        var ctx = new ConversationBuilder()
            .WithMaxTokens(200)
            .WithUserMessage("Do something")
            .WithToolResult("c1", "tool", new string('x', 300))
            .Build();
        ctx.SetSystemPrompt("You are helpful.");

        var result = strategy.Compact(ctx.Messages, budget: 200);

        result.Should().Contain(m => m.Role == Role.System,
            because: "system messages must always survive compaction");
    }
}

// Each strategy implementation inherits and plugs in its factory:
public class SlidingWindowStrategyContractTests : CompactionStrategyContractTests
{
    protected override ICompactionStrategy CreateStrategy()
        => new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 5));
}

public class LlmSummarizationStrategyContractTests : CompactionStrategyContractTests
{
    protected override ICompactionStrategy CreateStrategy()
        => new LlmSummarizationStrategy(new FakeLlmClient(responseTokens: 50));
}
```

When adding a new strategy, create a one-line subclass and get the full contract suite
for free. If a contract test doesn't apply to a specific strategy, override and skip it
with a clear reason — never silently delete it.

---

## Conditional Test Skipping

Some tests depend on runtime conditions: an API key being set, a service being reachable,
or a specific OS. Use conditional skipping to make these tests self-documenting rather
than failing with cryptic errors.

xUnit doesn't ship a `[ConditionalFact]` attribute, but you can build one trivially
with `Skip`:

```csharp
[Fact(Skip = "Requires OPENROUTER_API_KEY")]  // static skip — always skipped
```

For dynamic skipping based on runtime checks, use the `Skip` property on xUnit v3 or
a helper that throws `SkipException`:

```csharp
public static class TestEnvironment
{
    public static string? OpenRouterApiKey =>
        Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");

    public static bool HasOpenRouterKey => OpenRouterApiKey is not null;
}

// In xUnit v2: use a custom [ConditionalFact] attribute or check-and-return
[Trait("Category", "E2E")]
[Fact]
public async Task AgentLoop_CompactsAndContinues()
{
    if (!TestEnvironment.HasOpenRouterKey)
    {
        // xUnit v2: output a message and return early
        // xUnit v3: throw new SkipException("...")
        return;
    }

    var client = new OpenRouterClient(TestEnvironment.OpenRouterApiKey!);
    // ... rest of e2e test
}
```

Use conditional skipping for:

- **E2E tests** that need API keys (OpenRouter, Anthropic)
- **Platform-specific tests** (e.g., token counting behavior on Windows vs Linux due to line endings)
- **Slow tests** you want to opt-in to locally via an environment flag

Do not use conditional skipping to hide broken tests. If a test is broken, fix it or
delete it.

---

## Shared Fixtures for Expensive Setup

Some test resources are expensive to create — a configured `ConversationContext`, a
fake LLM client with a large fixture response set, or a pre-built message history. Use
xUnit's `IClassFixture<T>` to share these across tests in a class without recreating
them per test.

```csharp
public class LargeConversationFixture
{
    public ConversationContext Context { get; }

    public LargeConversationFixture()
    {
        var builder = new ConversationBuilder()
            .WithMaxTokens(100_000)
            .WithStrategy(new SlidingWindowStrategy(
                new SlidingWindowOptions(windowSize: 20)));

        // Simulate a 50-turn conversation
        for (int i = 0; i < 50; i++)
        {
            builder.WithUserMessage($"Turn {i}: do something");
            builder.WithToolResult($"call-{i}", "execute", new string('x', 1500));
        }

        Context = builder.Build();
    }
}

public class SlidingWindowOnLargeHistoryTests : IClassFixture<LargeConversationFixture>
{
    private readonly LargeConversationFixture _fixture;

    public SlidingWindowOnLargeHistoryTests(LargeConversationFixture fixture)
        => _fixture = fixture;

    [Fact]
    public void Prepare_KeepsRecentTurnsIntact()
    {
        var prepared = _fixture.Context.Prepare();

        prepared.TakeLast(20).Should().OnlyContain(
            m => m.CompactionState == CompactionState.Original,
            because: "the 20 most recent turns are inside the window");
    }
}
```

Rules for fixtures:

- **Read-only tests only.** If a test mutates fixture state, other tests become order-dependent.
  For tests that mutate, build a fresh instance per test (via the builder).
- **No collection fixtures for parallelism.** xUnit collection fixtures disable parallel
  execution for all classes in the collection. Prefer class fixtures unless you genuinely
  need cross-class sharing.
- **Lock-and-flag for truly global setup.** If multiple fixtures need to initialize the
  same singleton (rare in TokenGuard since there's no DB), use the lock pattern:

```csharp
private static readonly object _lock = new();
private static bool _initialized;

public MyFixture()
{
    lock (_lock)
    {
        if (!_initialized)
        {
            // one-time expensive setup
            _initialized = true;
        }
    }
}
```

---

## Separate Mutation from Verification

When a test mutates state and then reads it back to assert, be careful not to assert
against cached or in-memory state that was never actually committed. This is a general
principle, not just a database concern.

```csharp
// Bad: asserting on the same object reference we just mutated
ctx.AddUserMessage("new message");
ctx.Messages.Last().Content.Should().Be("new message"); // trivially true, tests nothing

// Good: round-trip through the public API
ctx.AddUserMessage("new message");
var prepared = ctx.Prepare(); // the real public surface
prepared.Last().Segments.OfType<TextContent>().First().Text
    .Should().Be("new message");
```

The general rule: **never assert on the same object you just wrote to.** Assert on
what a consumer of the API would actually see — the return value of `Prepare()`, the
contents of a `CompactionEvent`, or the output of a provider adapter mapping.

---

## Common Smells and Fixes

| Smell | Fix |
|---|---|
| Test name is the method name (`SaveOrder_Test`) | Rename to behavior: `Compact_WhenThresholdExceeded_ReducesTokenCount` |
| 20-line Arrange block | Extract a builder or factory method (`ConversationBuilder`) |
| `Thread.Sleep` in integration/E2E test | Use `WaitForAsync` / polling / event-driven assertions |
| Asserting on `mock.Verify` for every call | Assert on outcome state; verify only for side effects with no other observable signal |
| Tests sharing a static mutable collection | Reset in constructor / `[BeforeEach]`, or use immutable seeds |
| E2E test asserts on exact LLM output text | Assert structurally: token count dropped, required keys present, no exception thrown |
| E2E test runs in standard CI | Gate with `[Trait("Category", "E2E")]` and a separate pipeline step |
| Same strategy contract tested differently per impl | Extract an abstract spec base class; each impl inherits and overrides only the factory |
| Test silently passes when API key is missing | Use conditional skip so the test shows as "skipped" in reports, not "passed" |
| Fixture builds expensive state but tests mutate it | Split: read-only tests use the fixture; mutating tests build their own via the builder |

---

## Framework Quick Reference

### .NET (xUnit + FluentAssertions + NSubstitute)

```csharp
// Fake over mock — encodes behavioral contract, not call count
var llmClient = new FakeLlmClient(responseTokens: 120);

// Fluent assertion with a reason
result.Messages.Should().HaveCountLessThan(before,
    because: "compaction should have reduced the message list");

// Equivalence ignoring non-deterministic fields
result.Should().BeEquivalentTo(expected, opts => opts
    .Excluding(x => x.ResponseId)
    .Excluding(x => x.CreatedAt));
```

### Gating E2E Tests (Live LLM API)

E2E tests call real APIs, cost real money, and can be throttled or flaky due to
network conditions. They must never run in standard CI.

```csharp
// Mark every E2E test with a trait
[Trait("Category", "E2E")]
public class SemanticFoldE2ETests
{
    [Fact]
    public async Task AgentLoop_WhenContextExceedsThreshold_CompactsAndContinues()
    {
        // Arrange — real client, real key from environment
        var apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")
            ?? throw new InvalidOperationException("E2e API key not set.");

        var client = new OpenRouterClient(apiKey);
        var fold = new SemanticFoldContext(client, new SlidingWindowStrategy(maxTokens: 2000));

        // Act — drive the loop until compaction must occur
        for (int i = 0; i < 20; i++)
            await fold.SendAsync($"Message {i}: " + new string('x', 200));

        // Assert structurally — never assert on exact LLM text
        fold.TokenCount.Should().BeLessThanOrEqualTo(2000,
            because: "compaction must have fired before this point");
        fold.Messages.Should().NotBeEmpty(
            because: "at least a summary message must survive compaction");
        fold.CompactionCount.Should().BeGreaterThan(0);
    }
}
```

Filter them out of your default CI run and into a dedicated nightly or manual pipeline:

```bash
# Run everything except E2E
dotnet test --filter "Category!=E2E"

# Run only E2E (nightly / manual trigger)
dotnet test --filter "Category=E2E"
```

Store API keys in CI secrets, never in source. Treat a failing e2e test as a
signal to investigate — not as a hard build-breaker, since transient API errors
are outside your control.

---

## Checklist Before Submitting Tests

- [ ] Test name describes behavior and expected outcome
- [ ] AAA structure is clearly identifiable
- [ ] Only one behavior is tested
- [ ] No I/O in unit tests; state is owned in integration/E2E tests
- [ ] Time, randomness, and external calls are controlled
- [ ] Failure message would be informative without reading the source code
- [ ] Test passes reliably when run in isolation and in parallel
- [ ] Contract behaviors are in a shared spec base, not duplicated per implementation
- [ ] Tests that need external resources skip cleanly when those resources are absent
- [ ] Builders are used when Arrange exceeds ~5 lines
- [ ] Assertions verify API output, not the same object that was mutated
