# Changelog

All notable changes to TokenGuard are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Optional logging. `ConversationConfigBuilder.WithLoggerFactory(ILoggerFactory)` supplies a logger factory, and contexts
  created through `AddConversationContext(...)` use the container's `ILoggerFactory` automatically. Records have stable
  event IDs and cover prepare calls, compaction decisions, summarizer calls, and emergency truncation.
- Tracing and metrics on an `ActivitySource` and a `Meter`, both named `TokenGuard`. The new public class
  `TokenGuardDiagnostics` exposes the names as `ActivitySourceName` and `MeterName`.
- Conversation health signals (estimator drift, repeated compaction, low yield, summarization failure streak, repeated
  over-budget, pinned pressure, checkpoint churn) reported as log records and on the `tokenguard.health.signals` counter,
  plus one summary record when a context is disposed.
- `docs/observability.md`, the reference for event IDs, activities, instruments, health signals, and the privacy rules.

### Changed
- `TokenGuard.Core` now depends on `Microsoft.Extensions.Logging.Abstractions`.

## [1.0.0] - 2026-06-01

Initial public release of `TokenGuard.Core`, `TokenGuard.Extensions.OpenAI`,
and `TokenGuard.Extensions.Anthropic`.

### Added
- Token-budget tracking for LLM agent loops via `ConversationContext` and `PrepareAsync`.
- Tiered compaction pipeline: always-on sliding-window masking, optional LLM summarization,
  and a last-resort emergency truncation safety net.
- Heuristic token counting with provider input-token anchoring.
- OpenAI and Anthropic extension packages for message conversion and provider-backed summarization.
- Dependency-injection registration and factory-based creation with named profiles.
- Configurable budget thresholds, overrun tolerance, and sliding-window options, all validated at construction.
- `PrepareResult.SummarizationError`: when the optional LLM summarizer fails (rate-limit, timeout,
  network error), TokenGuard degrades to sliding-window masking instead of crashing the agent loop and
  reports the captured exception for logging.

[1.0.0]: https://github.com/svetstoykov/TokenGuard/releases/tag/v1.0.0
