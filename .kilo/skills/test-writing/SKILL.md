---
name: testing-principles
description: >
  Apply best practices for writing unit, integration, and end-to-end (e2e) tests.
  Use this skill whenever the user asks for help writing, reviewing, or structuring
  tests of any kind — including xUnit, NUnit, MSTest, Vitest, or any other framework.
  Trigger on phrases like "write a test", "add unit tests", "review my tests",
  "how should I test this", "help with integration tests", "e2e test setup", or any
  request to improve test coverage or test quality. E2e in this context means running
  a real agent loop against a live LLM API (OpenRouter, Anthropic, etc.), not browser
  automation. Also trigger when the user shares existing test code that smells wrong
  or is hard to maintain — even if they don't explicitly ask for a review.
---

# Testing Principles

A decision-making and code-generation guide for writing high-quality unit, integration,
and e2e tests. These five principles apply across all frameworks and languages but
examples are given in C#/.NET (xUnit + FluentAssertions) to reflect the primary usage
context. E2e in this context means running a real agent loop against a live LLM API
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
| **Integration** | Own your state. Use transactions rolled back after each test, or dedicated test schemas. Swap real LLM clients for deterministic fakes at the boundary. |
| **E2e** | Real LLM API calls (OpenRouter, Anthropic, etc.). Gate behind a `[Trait]` category so they never run in standard CI. Each test constructs its own conversation from scratch — no shared state. Assert structurally, not on exact text. |

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

| Question | Unit | Integration | E2e |
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
for pure logic and a lean set of e2e tests for critical paths.

---

## Common Smells and Fixes

| Smell | Fix |
|---|---|
| Test name is the method name (`SaveOrder_Test`) | Rename to behavior: `Compact_WhenThresholdExceeded_ReducesTokenCount` |
| 20-line Arrange block | Extract a builder or factory method (`ConversationBuilder`) |
| `Thread.Sleep` in integration/e2e test | Use `WaitForAsync` / polling / event-driven assertions |
| Asserting on `mock.Verify` for every call | Assert on outcome state; verify only for side effects with no other observable signal |
| Tests sharing a static mutable collection | Reset in constructor / `[BeforeEach]`, or use immutable seeds |
| E2e test asserts on exact LLM output text | Assert structurally: token count dropped, required keys present, no exception thrown |
| E2e test runs in standard CI | Gate with `[Trait("Category", "E2e")]` and a separate pipeline step |

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

### Gating E2e Tests (Live LLM API)

E2e tests call real APIs, cost real money, and can be throttled or flaky due to
network conditions. They must never run in standard CI.

```csharp
// Mark every e2e test with a trait
[Trait("Category", "E2e")]
public class SemanticFoldE2eTests
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
# Run everything except e2e
dotnet test --filter "Category!=E2e"

# Run only e2e (nightly / manual trigger)
dotnet test --filter "Category=E2e"
```

Store API keys in CI secrets, never in source. Treat a failing e2e test as a
signal to investigate — not as a hard build-breaker, since transient API errors
are outside your control.

---

## Checklist Before Submitting Tests

- [ ] Test name describes behavior and expected outcome
- [ ] AAA structure is clearly identifiable
- [ ] Only one behavior is tested
- [ ] No I/O in unit tests; state is owned in integration/e2e tests
- [ ] Time, randomness, and external calls are controlled
- [ ] Failure message would be informative without reading the source code
- [ ] Test passes reliably when run in isolation and in parallel