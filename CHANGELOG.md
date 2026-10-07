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
- `TokenGuard.Extensions.OpenAI` now requires `OpenAI` 2.14.0 or later (was 2.10.0).
- `TokenGuard.Extensions.Anthropic` now requires `Anthropic` 12.53.0 or later (was 12.13.0).
- `new SlidingWindowOptions()` returns the documented defaults, the same value as `SlidingWindowOptions.Default`. It used
  to return the zero value, which could not be used: masking a tool result threw from `string.Format`.
- `ConversationConfigBuilder.Build()` rejects configurations it used to accept. A compaction or emergency threshold that
  is `NaN` throws `ArgumentOutOfRangeException`, and a `default(SlidingWindowOptions)` or
  `default(LlmSummarizationOptions)` throws `ArgumentException` saying the options were not initialized.
  `SlidingWindowOptions` rejects a `NaN` `protectedWindowFraction` at construction.

### Fixed
- The OpenAI examples in the root, `TokenGuard.Core`, and `TokenGuard.Extensions.OpenAI` READMEs declare the completion
  as `ChatCompletion`, so they compile as written. The `TokenGuard.Core` README lists both packages its quick start needs.
- `PrepareResult.Messages` is a separate list on every path. Below the compaction trigger it used to be the context's own
  history list, so it grew when more messages were recorded and emptied when the context was disposed.
- Threshold rejections name the setting that is wrong. An emergency threshold outside `(0.0, 1.0]` is reported as
  `emergencyThreshold` (it used to be reported as a compaction threshold error), and the message for a compaction
  threshold that is not below the emergency threshold includes both values, so the default emergency threshold of 1.0
  is visible.

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
- Configurable budget thresholds, overrun tolerance, and sliding-window options, range-checked at construction.
- `PrepareResult.SummarizationError`: when the optional LLM summarizer fails (rate-limit, timeout,
  network error), TokenGuard degrades to sliding-window masking instead of crashing the agent loop and
  reports the captured exception for logging.

[1.0.0]: https://github.com/svetstoykov/TokenGuard/releases/tag/v1.0.0
