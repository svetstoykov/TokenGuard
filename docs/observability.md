# Observability

TokenGuard reports what it does in three ways: log records, tracing activities, and metrics. All three are off until you
connect something to them, and none of them changes how a conversation is prepared.

- **Logs** go to the `ILoggerFactory` you supply. They tell you what happened in one conversation.
- **Activities** are started on an `ActivitySource` named `TokenGuard`. They tell you where the time goes.
- **Metrics** are recorded on a `Meter` named `TokenGuard`. They tell you how TokenGuard behaves across all conversations.

TokenGuard writes only to these standard .NET types. It has no sink, exporter, or console output of its own.

## Enabling logging

### With dependency injection

Register logging and call `AddConversationContext`. Contexts created by the resolved `IConversationContextFactory` log
through the container's `ILoggerFactory` without further setup. Resolve `IConversationContextFactory` from the container;
the concrete `ConversationContextFactory` registered beside it does not take the container's logger factory.

```csharp
services.AddLogging();
services.AddConversationContext(builder => builder.WithMaxTokens(100_000));
```

### Without dependency injection

Pass a logger factory to the builder.

```csharp
var configuration = new ConversationConfigBuilder()
    .WithMaxTokens(100_000)
    .WithLoggerFactory(loggerFactory)
    .Build();

var factory = new ConversationContextFactory(configuration);
```

A logger factory set with `WithLoggerFactory` takes precedence over the container's. With no logger factory at all,
TokenGuard writes no log records.

### Categories

Each type logs under its own full name, so one `TokenGuard` prefix covers the whole library.

| Category | Writes |
| --- | --- |
| `TokenGuard.Core.ConversationContext` | Recorded messages, prepare calls, emergency truncation, every health signal except `CheckpointChurn`, the conversation summary |
| `TokenGuard.Core.Strategies.TieredCompactionStrategy` | Which compaction stage produced the result |
| `TokenGuard.Core.Strategies.SlidingWindowStrategy` | Masking of old tool results |
| `TokenGuard.Core.Strategies.LlmSummarizationStrategy` | Summarization paths, skips, checkpoint changes, and the `CheckpointChurn` health signal |
| `TokenGuard.Extensions.OpenAI.OpenAISummarizer` | OpenAI summarizer calls |
| `TokenGuard.Extensions.Anthropic.AnthropicSummarizer` | Anthropic summarizer calls |

### Conversation ID, context name, and turn

Every context gets a conversation ID when it is created. Records from `ConversationContext` carry `ConversationId` and
`ContextName` as structured properties, and records written during `PrepareAsync()` also carry `Turn`. `ContextName` is the
name the configuration was registered under, or `default` for the default configuration. `Turn` counts the prepare calls
that saw new history: it goes up by one on each `PrepareAsync()` call made after a message was recorded. The
`tokenguard.turn` tag and the `Turns` figure of the `ConversationSummary` record count the same thing. It is a different
thing from the turn groups that emergency truncation drops, which come from message roles.

Records from the strategies and summarizers get the same three properties from a logging scope that the context opens
around the compaction strategy. To see them on those records, turn on scopes in your logging provider (for example
`IncludeScopes` for the console provider).

## What each level contains

| Level | Contents |
| --- | --- |
| `Trace` | One record per recorded message and one per masked tool result. A long conversation produces thousands of lines. |
| `Debug` | Every compaction decision: below-trigger prepare calls, the sliding-window pass, the summarization path and why it skipped, checkpoint changes, the tiered result, the emergency truncation evaluation, summarizer calls, estimate corrections, and the provider correction included in each prepare call's totals. Nothing at this level is written per message. |
| `Information` | One record for each prepare call that ran the compaction strategy, one when a health signal clears, and one summary when a context that was prepared at least once is disposed. |
| `Warning` | Emergency truncation removed messages, a summarization failure was absorbed, or a health signal started. |
| `Error` | The prepared payload is over budget, pinned messages alone exceed the maximum, summarization keeps failing, or prepare calls keep ending over budget. |

A `Debug` log of one conversation is enough to reconstruct every compaction decision.

## Event IDs

Ranges: 1000 to 1999 context lifecycle, 2000 to 2999 sliding window, 3000 to 3999 summarization, 4000 to 4999 tiered
strategy, 5000 to 5999 provider summarizers, 6000 to 6999 health signals.

| Event ID | Name | Level | Meaning |
| --- | --- | --- | --- |
| 1000 | MessageRecorded | Trace | A message was recorded. Carries role, pinned flag, and segment count. |
| 1001 | EstimateAnchored | Debug | The provider reported its input tokens. Carries the provider total, the last estimate (the total reported for the last prepared payload, with the correction then active), and the signed correction applied to later estimates (the provider total minus the summed message estimates of that payload). |
| 1010 | PrepareBelowTrigger | Debug | A prepare call returned the history unchanged because the total is below the compaction trigger. Carries total, trigger, and maximum tokens. |
| 1011 | CompactionCompleted | Information | A prepare call ran the compaction strategy. Carries outcome, tokens before and after, messages compacted, messages dropped by emergency truncation, strategy name, and elapsed milliseconds. |
| 1012 | EmergencyTruncationApplied | Warning | Emergency truncation removed messages. Carries the count and the token totals before and after. |
| 1013 | SummarizationFailed | Warning | The strategy reported a summarization failure and returned a result without a summary. The exception is attached. |
| 1014 | PrepareOverBudget | Error | The prepared payload exceeds the effective maximum (maximum tokens plus overrun tolerance). Carries the outcome, final tokens, and the effective maximum. |
| 1015 | PinnedBudgetExceeded | Error | Pinned messages alone exceed the maximum. Written before `PinnedTokenBudgetExceededException` is thrown. |
| 1016 | EmergencyTruncationEvaluated | Debug | The prepared payload exceeded the emergency trigger. Carries current tokens, the trigger, drop units considered and dropped (`TurnGroups` and `TurnGroupsDropped`: each whole turn group before the newest one, and each user message or model message with its tool results inside the newest one), the index of the first unpinned message that is always kept (`PreservedFloorIndex`: the user message that opened the tool loop when the payload ends with a tool result, otherwise the start of the preserved floor), and whether the kept messages alone still exceed the trigger. |
| 1017 | PinnedMessagePlaced | Debug | One pinned message was placed in a prepared payload that was reassembled around pinned messages. Carries the message's index in the recorded history and its index in the prepared payload. |
| 1018 | ProviderCorrectionApplied | Debug | A prepare call included a provider correction in its token totals. Carries the correction, the summed message estimates of the payload the provider measured (`CorrectionBaseTokens`), and the part of the correction included in the total before compaction and in the total of the prepared payload. Written on every prepare call while the correction is not zero. |
| 2000 | SlidingWindowApplied | Debug | One sliding-window pass. Carries message count, available tokens, tokens before and after, window size, protected messages, and tool results masked. |
| 2001 | ToolResultMasked | Trace | One tool result was replaced with a placeholder. Carries message index, tool call ID, tool name, and the message's tokens before and after. |
| 3000 | SummarizationPathSelected | Debug | Summarization started with or without a reusable checkpoint. Carries the protected tail's first index and size, the number and token total of older messages, and the target summary size. |
| 3010 | SummarizationSkippedNothingToSummarize | Debug | History returned unchanged: every message is inside the protected tail. |
| 3011 | PromotedSummaryOvershot | Debug | History returned unchanged: a summary extended to more messages still exceeded the available tokens. |
| 3012 | RefreshedSummaryOvershot | Debug | History returned unchanged: a smaller rewrite of the saved summary still exceeded the available tokens. |
| 3013 | SummarizationSkippedInsufficientBudget | Debug | History returned unchanged: the tokens left after the protected tail are below the minimum summary size. |
| 3014 | FirstSummaryOvershot | Debug | History returned unchanged: the first summary still exceeded the available tokens. |
| 3020 | SummaryCheckpointCreated | Debug | A summary checkpoint was saved or replaced. |
| 3021 | SummaryCheckpointReused | Debug | A saved checkpoint was reused without calling the summarizer. |
| 3022 | SummaryCheckpointCleared | Debug | A saved checkpoint was discarded. `Reason` is `HistoryShorterThanCheckpoint` or `SummarizedPrefixChanged`. |
| 4000 | TieredResultSelected | Debug | Which stage's result the tiered strategy returned and why. `Reason` is `SlidingWindowSufficient`, `NoSummarizerConfigured`, `SummarizationSucceeded`, `SummarizationOvershot`, or `SummarizationThrew`. |
| 5000 | SummarizerCallStarting | Debug | A provider summarizer request is about to be sent. Carries provider, message count, target tokens, and the model when the summarizer knows it. |
| 5001 | SummarizerCallCompleted | Debug | A provider summarizer request returned. Carries elapsed milliseconds, summary length in characters, and provider-reported input and output tokens. |
| 5002 | SummarizerCallFailed | Debug | A provider summarizer request failed. Carries elapsed milliseconds and the exception type. The exception itself is logged once, by event 1013. |
| 6001 | EstimatorDriftDetected | Warning | Health signal started: the token estimate differs from the provider-reported value by more than the threshold. |
| 6002 | RepeatedCompactionDetected | Warning | Health signal started: the strategy ran on consecutive turns. Carries the tokens reclaimed on each. |
| 6003 | LowCompactionYieldDetected | Warning | Health signal started: a strategy run reclaimed almost nothing. |
| 6004 | SummarizationFailureStreakDetected | Error | Health signal started: summarization failed on consecutive strategy runs. |
| 6005 | RepeatedOverBudgetDetected | Error | Health signal started: consecutive prepare calls ended over budget. |
| 6006 | PinnedPressureDetected | Warning | Health signal started: pinned messages use a large share of the maximum. |
| 6007 | CheckpointChurnDetected | Warning | Health signal started: the summary checkpoint was cleared and rebuilt on consecutive summarization runs. |
| 6050 | HealthSignalCleared | Information | The condition behind a health signal stopped holding. `Signal` names it. |
| 6100 | ConversationSummary | Information | A context was disposed. Carries turns, prepare calls, strategy runs, tokens reclaimed, summarizer calls and failures, emergency truncations, peak prepared tokens, and the largest estimator drift. |

## Activities

Subscribe to the source by name. No activity object is created while nothing listens.

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(TokenGuardDiagnostics.ActivitySourceName))
    .WithMetrics(metrics => metrics.AddMeter(TokenGuardDiagnostics.MeterName));
```

TokenGuard does not reference OpenTelemetry. The snippet needs the OpenTelemetry packages in your application; an
`ActivityListener` and a `MeterListener` work as well.

| Activity | Covers | Tags |
| --- | --- | --- |
| `tokenguard.prepare` | One `PrepareAsync()` call | `tokenguard.conversation.id`, `tokenguard.context.name`, `tokenguard.turn`, `tokenguard.tokens.max`, `tokenguard.tokens.before`, `tokenguard.tokens.after`, `tokenguard.outcome`, `tokenguard.messages.compacted` |
| `tokenguard.compact` | One compaction strategy call. Child of `tokenguard.prepare`. | `tokenguard.strategy`, `tokenguard.tokens.available`, `tokenguard.tokens.before`, `tokenguard.tokens.after`, `tokenguard.messages.affected` |
| `tokenguard.summarize` | One summarizer call made by the LLM summarization strategy. Child of `tokenguard.compact`. | `tokenguard.messages.count`, `tokenguard.tokens.target` |

`tokenguard.tokens.before` and `tokenguard.tokens.after` on `tokenguard.prepare` are the `TokensBeforeCompaction` and
`TokensAfterCompaction` of the returned `PrepareResult`. Both include the provider correction when one is known, as do
the token figures of events 1010, 1011, 1012, 1014, and 1016, the `tokenguard.context.tokens` and
`tokenguard.compaction.tokens_reclaimed` measurements, and the health signals. The same tags on `tokenguard.compact`,
and the figures logged by the strategies, are sums of per-message estimates without the correction.

Status and events:

- `tokenguard.prepare` ends with status `Ok`, or `Error` when the outcome is `CompactionInsufficient` or `CannotCompact`,
  or when `PinnedTokenBudgetExceededException` is thrown.
- Emergency truncation adds a `tokenguard.emergency_truncation` event to `tokenguard.prepare` with the tag
  `tokenguard.messages.dropped`.
- A summarizer call that throws sets status `Error` on `tokenguard.summarize` and adds an `exception` event. When the
  strategy absorbs the failure, `tokenguard.prepare` still ends with `Ok`, because the call succeeded.

## Metrics

Every instrument is tagged with `tokenguard.context.name`. No instrument carries the conversation ID or any other
per-conversation value. Durations are measured only while the instrument has a listener.

| Instrument | Type | Unit | Additional tags | Records |
| --- | --- | --- | --- | --- |
| `tokenguard.prepare.count` | Counter | `{call}` | `tokenguard.outcome` | Every prepare call that returns a result |
| `tokenguard.prepare.duration` | Histogram | `s` | `tokenguard.outcome` | Every prepare call that returns a result |
| `tokenguard.compaction.duration` | Histogram | `s` | `tokenguard.strategy` | Every compaction strategy call |
| `tokenguard.summarization.duration` | Histogram | `s` | `tokenguard.result` (`success`, `failure`) | Every summarizer call |
| `tokenguard.token_counting.duration` | Histogram | `s` | none | Once per prepare call: the time spent counting tokens during that call |
| `tokenguard.context.tokens` | Histogram | `{token}` | `tokenguard.outcome` | Every prepare call that returns a result: the estimated size of the prepared payload |
| `tokenguard.compaction.tokens_reclaimed` | Histogram | `{token}` | none | Every prepare call that ran the strategy: tokens before minus tokens after |
| `tokenguard.compaction.messages` | Counter | `{message}` | `tokenguard.kind` (`masked`, `summarized`, `dropped`) | Every prepare call that ran the strategy: messages changed, by kind |
| `tokenguard.summarization.failures` | Counter | `{failure}` | none | Every summarizer call that threw |
| `tokenguard.emergency_truncation.count` | Counter | `{truncation}` | none | Every prepare call in which emergency truncation removed messages |
| `tokenguard.estimate.error_ratio` | Histogram | `1` | none | Every `RecordModelResponse` call that supplies `providerInputTokens`: the provider value minus the total reported for the last prepared payload, divided by the provider value |
| `tokenguard.health.signals` | Counter | `{signal}` | `tokenguard.signal` | Each time a health signal starts |

A prepare call that throws records no prepare measurement. A summarizer call that is cancelled by the caller records
neither a duration nor a failure.

## Health signals

A health signal is a pattern across turns that a single record cannot show. Each context watches for these on its own
conversation. TokenGuard only reports them; it never changes thresholds, switches strategies, or retries.

Each signal writes one record and increments `tokenguard.health.signals` when its condition starts to hold. It writes
nothing more while the condition keeps holding, and one `HealthSignalCleared` record when it stops.

| Signal | Event ID | Level | Starts when |
| --- | --- | --- | --- |
| `EstimatorDrift` | 6001 | Warning | The estimate differs from the provider-reported input tokens by more than 10% of the provider value |
| `RepeatedCompaction` | 6002 | Warning | The strategy ran on 3 consecutive turns |
| `LowCompactionYield` | 6003 | Warning | The strategy ran and reclaimed less than 5% of the tokens it started with |
| `SummarizationFailureStreak` | 6004 | Error | A summarization failure was reported on 3 consecutive strategy runs |
| `RepeatedOverBudget` | 6005 | Error | 2 consecutive prepare calls ended `CompactionInsufficient` or `CannotCompact` |
| `PinnedPressure` | 6006 | Warning | Pinned messages use more than 50% of the maximum tokens |
| `CheckpointChurn` | 6007 | Warning | The summary checkpoint was cleared and rebuilt on 3 consecutive summarization runs. Logged under the `TokenGuard.Core.Strategies.LlmSummarizationStrategy` category |

`LowCompactionYield` and `RepeatedCompaction` often start together but have different causes: a protected tail that is too
large, and a budget that is too small for the workload. The thresholds are fixed in this release.

A single summarization failure is a `Warning` (event 1013). A streak of them is an `Error` (event 6004).

When a context that had `PrepareAsync()` called at least once is disposed, it writes one `ConversationSummary` record with
the totals for the conversation.

## Privacy

TokenGuard never writes message text, system prompts, tool arguments, tool results, or summaries to logs, activities, or
metrics. Records carry counts, token totals, identifiers, and names.

- Tool names and tool call IDs are treated as metadata. They are written at `Debug` and `Trace`.
- Exceptions from provider SDKs are attached to log records and activity events as received. Their messages are outside
  TokenGuard's control and may contain provider-supplied text.

## Stable names

These are a contract. Renaming one is a breaking change:

- event IDs and event names
- activity names
- instrument names
- tag names

Message wording is not part of the contract. Build alerts and dashboards on event IDs and names, not on message text.
