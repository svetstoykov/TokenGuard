# Changelog

All notable changes to TokenGuard are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Known limits

- One tool result larger than the budget cannot be fitted. The prepare returns `CompactionInsufficient` when it masked,
  summarized, or dropped other messages, and `CannotCompact` when it changed none. In both the request is over the
  budget and the caller decides whether to send it.

### Changed

- The quick starts in the README and on the package pages stop on both over-budget outcomes, `CompactionInsufficient`
  and `CannotCompact`, and end after a set number of model calls.
- Every published example checks for a reply with no segments before it calls `RecordModelResponse`, which throws
  `ArgumentException` for one. The README and the XML documentation of `RecordModelResponse` state the rule.
- The README carries the complete Anthropic program and lists `Microsoft.Extensions.DependencyInjection` in the install
  lines. It gains a table of terms and a paragraph on choosing a budget.
- The NuGet page of `TokenGuard.Core` gains sections on the prepare result and its outcomes, pinned messages, LLM
  summarization, and token counting.
- The snippet check compiles the fragments of the `TokenGuard.Extensions.OpenAI` package readme, which now name the
  `TokenGuard.Core.Extensions` and `TokenGuard.Core.Options` namespaces they need.

## [1.2.0] - 2026-10-13

Released packages: `TokenGuard.Core`, `TokenGuard.Extensions.OpenAI`, and `TokenGuard.Extensions.Anthropic`, all at
1.2.0. Each extension depends on exactly `TokenGuard.Core` 1.2.0.

### Behavior changes

No public signature changed. A consumer upgrading from 1.1.0 can observe these differences; each is described in full
under Changed or Fixed below.

- The default token budget is 40,000 instead of 25,000, so a consumer relying on the default starts compacting at
  32,000 tokens instead of 20,000. A model whose context window is below 40,000 tokens needs an explicit budget.
- Tool-result masking moves its boundary in steps, so a prepared view differs from the one 1.1.0 produces for the same
  history, and the default placeholder of a masked result has a new, longer text.
- A recording call or a `PrepareAsync()` call that starts while another call on the same context is still running
  throws `InvalidOperationException`.
- A recording call discards an exception thrown by a logger or meter listener, where it used to pass it to the caller
  after storing the message.
- A summarizer call that failed is not repeated while the history and the available tokens stay the same.
- `TokenGuard.Extensions.OpenAI` and `TokenGuard.Extensions.Anthropic` depend on exactly the `TokenGuard.Core` version
  they were built against, so a mismatch is reported at restore.
- `TokenGuard.Extensions.Anthropic`: `ForAnthropic()` sends every text segment and every tool result, and throws
  `InvalidOperationException` for input it used to convert with content missing. `InputTokens()` returns the total
  input of the request including cached tokens, and `null` when the response carries no usage.

### Known limits

- Summarization works with the built-in providers only. A custom summarizer, compaction strategy, or token counter
  cannot be registered through the builder.
- One `ConversationContext` is for one caller at a time. Overlapping calls are detected on a best-effort basis and
  throw; calls from several threads that happen not to overlap are not reported.
- Token estimates leave out request overhead such as tool schemas until the first provider report. Pass
  `providerInputTokens` to `RecordModelResponse` to correct for it.
- The estimator undercounts pretty-printed JSON by 17% to 30% against the `o200k_base` tokenizer until the first
  provider report corrects it, and runs 15% to 49% high on source code and non-Latin text.
- One tool result larger than the window cannot be fitted. The prepare returns `CompactionInsufficient` and the caller
  decides whether to send the request.
- On a provider with a prompt cache the money saving differs from the token saving, because masking changes earlier
  messages and with them the share of the input billed at the cache-read price.
- `TokenGuard.Extensions.Anthropic` is covered by unit tests only. No run against the Anthropic API has verified it.
- The packages target `net10.0` only.

### Added

- The provider's finish reason and token counts on every summarizer answer, so an empty answer carries its cause. The
  `tokenguard.summarize` activity gains the tags `tokenguard.finish_reason`, `tokenguard.tokens.output`, and
  `tokenguard.tokens.reasoning`; events 5001 (`SummarizerCallCompleted`) and 5002 (`SummarizerCallFailed`) gain
  `FinishReason`, `ReasoningTokens`, and, on 5002, `OutputTokens`. Reasoning tokens are reported by the OpenAI summarizer
  only.
- A package icon on `TokenGuard.Core`, `TokenGuard.Extensions.OpenAI`, and `TokenGuard.Extensions.Anthropic`.

### Changed

- `TokenGuard.Extensions.OpenAI` and `TokenGuard.Extensions.Anthropic` depend on exactly the `TokenGuard.Core` version
  they were built against (`[x.y.z]` in the package dependency) instead of that version or any later one. Both
  extensions use internal members of Core, so pairing an extension with a different Core version could fail at run time
  with `MissingMethodException` or `TypeLoadException`. NuGet now reports the mismatch at restore: as a `NU1107`
  version conflict when two extensions pin different Core versions, and as a `NU1608` warning when the project also
  references a different Core version directly. Upgrading Core requires the extension release built against it.
  Extension versions 1.0.1 and 1.1.0 keep the open-ended dependency.
- The `InvalidOperationException` thrown for an empty summarizer answer states the finish reason and the output tokens
  in its message, for example `OpenAI summarization returned an empty answer. Finish reason: length; output tokens: 20;
  reasoning tokens: 19.` The exception is attached to event 1013 (`SummarizationFailed`) and returned as
  `PrepareResult.SummarizationError`.
- Tool-result masking moves its boundary in steps. Nothing is masked until the compactable history exceeds
  `SlidingWindowOptions.ProtectedWindowFraction` of the available tokens (80% by default), as before. Above that, the
  boundary used to be worked out again on every call and crept forward as the history grew; it now sits on a step line
  and holds until the history has grown by about one step, a quarter of that fraction (20% of the available tokens by
  default). Between two steps a prepared view is the previous view plus the new messages, so a provider prompt cache or
  a local model server can reuse the whole previous request. A prepared view masks further back at a step, so it
  differs from the one 1.1.0 produces for the same history, and the unmasked tail holds between 60% and 80% of the
  available tokens instead of staying close to 80%. The step is not configurable and no public member changed.
- The default `SlidingWindowOptions.PlaceholderFormat` is `[Tool result removed to save context — {0}, {1}. Call the
  tool again if you need this content.]` instead of `[Tool result cleared — {0}, {1}]`, so masked results carry the new
  text. `{0}` is still the tool name and `{1}` the tool call ID. A custom format is unaffected. The new text is longer,
  so a masked result costs a few more tokens than before.
- Behavior change in `TokenGuard.Extensions.Anthropic`: `ForAnthropic()` throws `InvalidOperationException` for input
  it used to convert with content missing. The message names the role and the problem. It throws for a user or system
  message with no text or with a segment that is not text; for a model message with a segment that is neither text nor
  a tool call, or with neither text nor a tool call; for a tool message with no tool result or with a segment that is
  not a tool result; for a tool result with no preceding tool call; for a tool call with no matching result before the
  next message that is not a tool message; and for tool calls unanswered at the end of the list. A system message with
  no text used to be skipped, a tool message with no result used to be left out, and unpaired tool calls and results
  used to be sent as they were. No public member changed.
- Behavior change in `TokenGuard.Extensions.Anthropic`: tool-call arguments that are not a JSON object make
  `ForAnthropic()` throw `InvalidOperationException` naming the tool call ID. It used to throw a `JsonException` with
  no TokenGuard message; that exception is now the inner exception when the arguments are not valid JSON.
- Behavior change in `TokenGuard.Extensions.Anthropic`: `InputTokens()` on an Anthropic `Message` returns `null` when
  the response carries no usage. It used to throw the SDK's `AnthropicInvalidDataException`.
- `tests/TokenGuard.ReadmeSnippets/verify.sh` packs `TokenGuard.Extensions.Anthropic` and compiles an Anthropic quick
  start, and release validation fails when `README.md` or the Anthropic package readme stops carrying its text. The
  Anthropic package readme has a complete quick start with the request and the recording of the response, and states
  that the adapter is covered by unit tests only.
- A recording call (`SetSystemPrompt`, `AddPinnedMessage`, `AddUserMessage`, `RecordModelResponse`, `RecordToolResult`)
  or a `PrepareAsync()` call that starts while another of those calls on the same context is still running throws
  `InvalidOperationException`. It used to succeed and could lose a message with no error. A `PrepareAsync()` call is
  running until its task completes, so await it before recording, and record the results of parallel tool calls one
  after another. The detection is best effort and a context is still for one caller at a time: calls from several
  threads that happen not to overlap are not reported. No public member changed.
- Behavior change: the default token budget is 40,000 instead of 25,000. It applies when no budget is given: to
  `ConversationConfigBuilder.Default()` called without an argument, and to the default profile that
  `AddConversationContext()` registers when called without a configuration or that a named registration creates when
  no default profile was registered first. A consumer relying on it gets a compaction trigger of 32,000 tokens instead
  of 20,000 and an emergency limit of 40,000 instead of 25,000, so requests carry more tokens before compaction starts,
  and the overrun tolerance is 2,000 tokens instead of 1,250. A model whose context window is below 40,000 tokens needs
  an explicit budget: `WithMaxTokens(...)`, or `ConversationConfigBuilder.Default(maxTokens)`. A budget set explicitly
  is unaffected, and no public member changed.
- The benchmark sections of the README and the deep dive state two measured results, each against a control run without
  compaction and each with its run IDs: 41% to 45% fewer agent input tokens on ten short verifiable tasks at a
  50,000-token window with TokenGuard 1.1.0, and 69.7% fewer input tokens on six long sessions with this release. They
  state the checks passed in each arm, the billed amounts apart from the token counts, and the limits of both
  measurements. The estimated 87.4% figure from the batch of 12 May 2026, which had no control run and checked no
  answer, is removed.

### Fixed

- Behavior change in `TokenGuard.Extensions.Anthropic`: `InputTokens()` on an Anthropic `Message` returns the total
  input of the request, the sum of `input_tokens`, `cache_creation_input_tokens` and `cache_read_input_tokens`. It
  used to return `input_tokens` alone, which with prompt caching is a small part of the request, so passing it to
  `RecordModelResponse` anchored the token estimates to too small a count. Without prompt caching the value is the
  same as before.
- Behavior change in `TokenGuard.Extensions.Anthropic`: `ForAnthropic()` sends every text segment of a user, model or
  system message as its own text block, in order, and every result of a tool message as a `tool_result` block in one
  user message. It used to send the first text segment and the first tool result only, so a recorded response with
  two text blocks came back as one. A request built from such messages is larger than before.
- A summarizer call that threw, or returned a summary too large for the budget, is not repeated while the history and
  the available tokens stay the same. Calling `PrepareAsync()` again without recording a message used to send a new
  summarizer request every time; it now sends none, returns the same fallback result, and logs the new `Debug` event
  3015. `SummarizationError` is set only on the call that made the request, and a skipped attempt adds nothing to
  `tokenguard.summarization.failures`. A recorded message or a changed budget allows a new attempt, and a call the
  caller cancelled is not remembered.
- Documentation states that a summarizer call has no time limit of its own: it is bounded only by the cancellation
  token passed to `PrepareAsync()` and by the provider client's network timeout.
- The examples in the root README and the three package READMEs use a 40,000-token window, so compaction starts at
  32,000 tokens. The OpenAI quick start is one program that runs as pasted into a new console project, with its package
  list, and the Observability section of the README has a program that prints log records, activities, and metrics and
  names the console logging provider and the console exporter it needs.
- Documentation states the overrun tolerance where it describes over-budget outcomes: a prepared request up to
  `MaxTokens` plus the tolerance (5% of `MaxTokens` by default) is `Compacted` or `Ready`, and only one above that is
  `CompactionInsufficient` or `CannotCompact`. It also states that preparing a context with no messages returns an
  empty `Ready` result, so the caller records at least one user message before the first model call.
- A masked tool result is labelled with the tool name recorded on that result. The name used to be looked up by tool
  call ID across the whole history, so when two tool calls shared an ID both placeholders named the later tool. The
  masked `ToolResultContent.ToolName` and the `{0}` argument of `SlidingWindowOptions.PlaceholderFormat` are now the
  name passed to `RecordToolResult`; a result whose tool call is not in the compacted history used to show its tool
  call ID in place of the name.
- A compaction strategy that throws out of `PrepareAsync()` is visible in diagnostics. The call used to leave no log
  record and both activities with status `Unset`; it now writes the new `Error` event 1019 (`StrategyFailed`) with the
  exception attached and sets status `Error` on `tokenguard.compact` and `tokenguard.prepare`. The caller receives the
  same exception as before, and a cancelled call (`OperationCanceledException`) writes no error record.
- A logger or meter listener that throws does not fail a recording call. `SetSystemPrompt`, `AddPinnedMessage`,
  `AddUserMessage`, `RecordModelResponse`, and `RecordToolResult` store the message, complete normally, and discard the
  exception from the diagnostics call. The exception used to reach the caller after the message was stored, so a caller
  that retried recorded the message twice. Exceptions from the token counter still reach the caller, and
  `PrepareAsync()` still surfaces an exception thrown by a logger or listener.

## [1.1.0] - 2026-10-07

Released packages: `TokenGuard.Core`, `TokenGuard.Extensions.OpenAI`, and `TokenGuard.Extensions.Anthropic`, all at
1.1.0.

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
- `TokenGuard.Extensions.Anthropic` now requires `Anthropic` 12.53.0 or later (was 12.13.0) and `TokenGuard.Core` 1.1.0
  or later.
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

[Unreleased]: https://github.com/svetstoykov/TokenGuard/compare/1.2.0...HEAD
[1.2.0]: https://github.com/svetstoykov/TokenGuard/releases/tag/1.2.0
[1.1.0]: https://github.com/svetstoykov/TokenGuard/releases/tag/1.1.0
[1.0.1]: https://github.com/svetstoykov/TokenGuard/releases/tag/1.0.1
[1.0.0]: https://github.com/svetstoykov/TokenGuard/releases/tag/1.0.0
