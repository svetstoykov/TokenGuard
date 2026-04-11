# Testing Principles

Apply best practices for writing unit, integration, and end-to-end tests. Use this guidance whenever the user asks for help writing, reviewing, or structuring tests of any kind, including xUnit, NUnit, MSTest, Vitest, or any other framework. Trigger on phrases like "write a test", "add unit tests", "review my tests", "how should I test this", "help with integration tests", and "E2E test setup". Also use it when the user shares existing test code that smells wrong or is hard to maintain.

End-to-end tests in this project mean running a real agent loop against a live LLM API such as OpenRouter or Anthropic, not browser automation.

Use the existing Kilo skill at `.kilo/skills/testing-principles/SKILL.md` as the authoritative detailed reference for the full testing workflow, examples, patterns, and checklists. When working in agents that do not natively load Kilo skills, follow that file directly.

## Core Principles

- Arrange-Act-Assert: every test should have a clear setup phase, a single action, and assertions on the observable outcome
- One behavior per test: each test should have one reason to fail and a name that describes the scenario and expected result
- Test behavior, not implementation: assert on public outcomes and contracts rather than internal call counts or incidental mechanics
- Isolation and determinism: control time, randomness, external I/O, and LLM boundaries so tests are reliable
- Meaningful failures: use readable assertions, descriptive naming, and structures that make CI failures actionable

## Layer Selection

- Use unit tests for pure logic, algorithms, and local decision-making
- Use integration tests for orchestration, message flow, strategy wiring, and components interacting through real boundaries with deterministic fakes at the edges
- Use end-to-end tests for live LLM validation only on critical paths, and gate them so they never run in standard CI

## TokenGuard-Specific Guidance

- Prefer builders when arrange sections become noisy, especially for conversation and message history setup
- Use contract-style tests for multiple implementations of the same abstraction, such as strategy interfaces
- Separate mutation from verification; assert through public APIs rather than the same object instance you just mutated
- Skip conditionally when external runtime requirements such as API keys are absent, but never use skipping to hide broken tests

## Reference

- Full detailed guidance, examples, and checklist: `.kilo/skills/testing-principles/SKILL.md`
