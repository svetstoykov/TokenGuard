# Changelog

All notable changes to TokenGuard are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.1.0] - 2026-10-07

Released packages: `TokenGuard.Core` 1.1.0 and `TokenGuard.Extensions.OpenAI` 1.1.0.
`TokenGuard.Extensions.Anthropic` stays at 1.0.1 on nuget.org; its source changes since 1.0.1 (the `Anthropic` 12.53.0
requirement and summarizer logging) ship with its next release.

### Behavior changes

No public signature changed. A consumer upgrading from 1.0.1 can observe these differences; each is described in full
under Changed or Fixed below.

- `PrepareResult.TokensAfterCompaction` includes the provider correction, so it is no longer the sum of `TokenCount`
  over `Messages`, and a result that used to be `Ready` or `Compacted` can be `CompactionInsufficient` or
  `CannotCompact`. Only conversations that pass `providerInputTokens` are affected.
- `ForOpenAI()` throws `InvalidOperationException` for a message it cannot represent and for tool calls left unanswered
  at the end of the list, where it used to shorten or drop content silently.
- `ConversationConfigBuilder.Build()` rejects `NaN` thresholds and default-constructed option structs, which it used to
  accept.
- Prepared views contain different messages in three cases: a pinned message is placed relative to its neighbours and
  never inside a tool exchange, a tool loop keeps the user message that opened it, and turn groups follow message
  structure instead of the timing of `PrepareAsync()` calls.

### Known limits

- The token counter, the summarizer, the summary formatter, and builder registration of a custom compaction strategy
  are closed to consumers. Summarization works with the built-in providers only.
- One `ConversationContext` is for one caller at a time. The rule is documented and not enforced.
- Token estimates leave out request overhead such as tool schemas. Pass `providerInputTokens` to
  `RecordModelResponse` to correct for it.
- The packages target `net10.0` only.

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
- `TokenGuard.Extensions.OpenAI` now requires `OpenAI` 2.14.0 or later (was 2.10.0) and `TokenGuard.Core` 1.1.0 or
  later.
- `new SlidingWindowOptions()` returns the documented defaults, the same value as `SlidingWindowOptions.Default`. It used
  to return the zero value, which could not be used: masking a tool result threw from `string.Format`.
- `ConversationConfigBuilder.Build()` rejects configurations it used to accept. A compaction or emergency threshold that
  is `NaN` throws `ArgumentOutOfRangeException`, and a `default(SlidingWindowOptions)` or
  `default(LlmSummarizationOptions)` throws `ArgumentException` saying the options were not initialized.
  `SlidingWindowOptions` rejects a `NaN` `protectedWindowFraction` at construction.
- `PrepareResult.TokensBeforeCompaction` and `TokensAfterCompaction` are on one scale, and the provider correction is
  kept across compaction. The correction is the `providerInputTokens` last passed to `RecordModelResponse` minus the
  summed message estimates of the payload it measured; it used to be dropped as soon as a prepare call ran the
  compaction strategy, before the outcome was decided. It now stays until the next provider report replaces it, and
  `TokensAfterCompaction` and the outcome include it: in full for a payload at least as large as the measured one, in
  proportion for a smaller one. What consumers see when they pass `providerInputTokens`:
  - `TokensAfterCompaction` is higher than before whenever the provider counts more than the estimate (lower when it
    counts less), and it is no longer equal to the sum of `TokenCount` over `Messages`. Reclaimed-token figures are
    smaller by the correction they used to overstate.
  - Some results change from `Ready` or `Compacted` to `CompactionInsufficient` or `CannotCompact`. Example with a
    maximum of 1,000: a 700-token user message that the provider measured at 1,300 used to come back `Ready` with 1,305
    tokens before and 711 after on an unchanged list; it is now `CannotCompact` with the same figure before and after.
  - A prepare call that follows a compaction without a new provider report still applies the correction, where it used
    to compare the uncorrected history with the compaction trigger. Emergency truncation applies the scaled correction
    to what is left after each dropped group, so it can drop fewer messages.
  - A second provider report below the trigger sets the correction against the message estimates. It used to be set
    against a total that already held the previous correction, which discarded most of it on every other report.
  - Log events 1010, 1011, 1012, 1014, and 1016, the `tokenguard.prepare` activity tags, `tokenguard.context.tokens`,
    `tokenguard.compaction.tokens_reclaimed`, and the health signals carry the same corrected figures.
    `tokenguard.estimate.error_ratio` and `EstimatorDrift` compare the provider count with the corrected total. The new
    `Debug` event 1018 `ProviderCorrectionApplied` states the correction included on each prepare call.
  Conversations that never pass `providerInputTokens` are unchanged.

### Fixed
- A pinned message is placed relative to its neighbours: after the surviving messages recorded before it and before
  those recorded after it, following the summary when the summary replaced the earlier messages. It used to go back to
  its absolute history index, so a pin added mid-conversation could land between a tool call and its result after
  summarization, and `ForOpenAI()` threw. A pinned message that would fall inside a tool exchange is placed before the
  model message that carries the tool calls. The new `Debug` event 1017 logs the prepared index of each pinned message.
- The benchmark figures in the README and the deep dive match the twenty retained transcripts: 128,188,640 and
  16,158,138 estimated prompt tokens over 1,325 prepare calls. Both token columns are labelled as TokenGuard estimates
  from the same sessions, no run without TokenGuard was measured, and the task-success and failure-prevention claims
  are removed.
- `ForOpenAI()` sends every text segment of a message, one content part per segment, where it used to send only the first.
  It throws `InvalidOperationException` for a message it cannot represent (a `Tool` message without a tool result used
  to be dropped silently) and for tool calls left unanswered at the end of the list.
- `ResponseSegments()` and `ToolUseSegments()` record a tool call with empty or whitespace arguments as `{}` instead of
  throwing.
- The OpenAI examples in the root, `TokenGuard.Core`, and `TokenGuard.Extensions.OpenAI` READMEs declare the completion
  as `ChatCompletion`, so they compile as written. The `TokenGuard.Core` README lists both packages its quick start needs.
- Documentation states that one context is for one caller at a time, that `PrepareAsync()` throws
  `PinnedTokenBudgetExceededException` when pinned messages alone exceed the budget, that the default profile is
  registered before named ones, and that `Ready` is also returned when a strategy ran and changed nothing.
  `SummarizationError` is in the `PrepareResult` tables, the `RecordToolResult` exception documentation matches the
  code, and the logging, platform, summarize-activity, and `CheckpointChurn` category statements match the code.
- `PrepareResult.Messages` is a separate list on every path. Below the compaction trigger it used to be the context's own
  history list, so it grew when more messages were recorded and emptied when the context was disposed.
- Threshold rejections name the setting that is wrong. An emergency threshold outside `(0.0, 1.0]` is reported as
  `emergencyThreshold` (it used to be reported as a compaction threshold error), and the message for a compaction
  threshold that is not below the emergency threshold includes both values, so the default emergency threshold of 1.0
  is visible.
- Turn groups come from the messages, not from when `PrepareAsync()` was called. A group starts at each unpinned user
  message and runs up to the next one. A history recorded without prepare calls in between, such as a restored
  conversation, used to be one group that emergency truncation dropped whole, leaving only the newest message; it now
  keeps the newest groups that fit. Views of conversations recorded turn by turn can change too: an older turn is
  dropped together with its user message, tool calls, and replies, and the summarization tail starts on the message
  the window size selects, moved back only to keep a tool call with its results.
- A tool loop keeps the user message that opened it. While the history ends with a tool result, emergency truncation
  no longer drops that message and LLM summarization keeps it word for word directly after the summary; both used to
  remove it and still report `Compacted`, so the model continued with no request in view. Tool-loop views under
  pressure are larger by that message, and when it does not fit together with the newest tool call and its results the
  outcome is `CompactionInsufficient` or `CannotCompact` where it used to be `Compacted`. The `PreservedFloorIndex` of
  log event 1016 is the index of that user message when it is kept.

## [1.0.1] - 2026-06-18

First nuget.org release of `TokenGuard.Extensions.OpenAI` and `TokenGuard.Extensions.Anthropic`, and a documentation
update of `TokenGuard.Core`. No code changed.

### Changed
- The package READMEs of all three packages are expanded with getting-started guidance: installation, registration,
  and the agent loop.

## [1.0.0] - 2026-06-18

Initial public release. `TokenGuard.Core` was published to nuget.org at this version; the two extension packages
followed at 1.0.1.

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

[Unreleased]: https://github.com/svetstoykov/TokenGuard/compare/1.1.0...HEAD
[1.1.0]: https://github.com/svetstoykov/TokenGuard/releases/tag/1.1.0
[1.0.1]: https://github.com/svetstoykov/TokenGuard/releases/tag/1.0.1
[1.0.0]: https://github.com/svetstoykov/TokenGuard/releases/tag/1.0.0
