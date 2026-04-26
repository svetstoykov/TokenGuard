# Audit Token Economy and Invariants from a Codexplorer Session Transcript

## Purpose

Analyze a Codexplorer session transcript and produce a structured audit report that checks token economy behavior, validates core invariants, and traces any violations back to the most likely source files and methods in the codebase.

This skill is for **detection and diagnosis only**. It does **not** fix code, write tests, or run the application.

## Scope

The input is a session transcript in the format produced by the Codexplorer sample application.

This skill must:

- Parse all `PrepareResult` blocks
- Parse all `Model response` blocks
- Compute aggregate and per-turn token economy metrics
- Check a defined set of correctness invariants against each turn
- Trace any invariant violation back to the relevant source files in the codebase
- Produce a structured audit report as a markdown file

This skill does **not**:

- Modify source code
- Fix bugs
- Write or run tests
- Execute the application

## Expected Input Format

The transcript contains blocks like:

```text
## Session Started
- Model: openai/gpt-5.4-nano
- Budget: ContextWindowTokens=16000, SoftThresholdRatio=0,8, HardThresholdRatio=1, WindowSize=5

## PrepareResult (turn N)
- TimestampUtc: ...
- TokensBeforeCompaction: <int>
- TokensAfterCompaction: <int>
- Outcome: Ready | Compacted | Degraded | ContextExhausted
- MessagesCompacted: <int>

## Model response (turn N)
- InputTokensReported: <int>
- OutputTokensReported: <int>
- TotalTokensReported: <int>
```

Not every turn has a `Model response` block. Tool-call-only turns may omit it. Missing response blocks must be handled gracefully.

## Required Outputs

Produce a markdown report file named:

`audit-<session-timestamp>.md`

The report must be self-contained and understandable without the transcript or codebase.

## Parsing Requirements

For every turn present in the transcript, extract:

- `turn` as integer
- `TokensBeforeCompaction`
- `TokensAfterCompaction`
- `Outcome`
- `MessagesCompacted`
- `InputTokensReported`
- `OutputTokensReported`
- `TotalTokensReported`

If a turn has no `Model response` block, leave the reporting fields blank or mark them as unavailable.

## Token Economy Metrics

Compute and report:

- Total cumulative `TokensBeforeCompaction` across all turns
- Total cumulative `TokensAfterCompaction` across all turns
- Total cumulative `InputTokensReported` across all turns
- Absolute input token savings: `Before - After`
- Percentage input token savings: `(Before - After) / Before`
- Per-turn savings at session end, using the final turn’s `Before` vs `After`
- Distribution of `Outcome` values with counts and percentages

## Invariant Checks

Evaluate every invariant below. If any violation occurs, report it explicitly with:

- turn number
- observed values
- a root-cause hypothesis

### 1. Monotonic history invariant

`TokensBeforeCompaction[turn N]` must be greater than or equal to `TokensBeforeCompaction[turn N-1]` for all consecutive turns.

A decrease means `_history` was mutated between turns, which violates the source-of-truth contract.

### 2. Budget hard-limit invariant

`InputTokensReported` must be less than or equal to `ContextWindowTokens` from the session header.

Any turn where the model received more tokens than the configured budget is a hard violation.

### 3. Tokenizer accuracy invariant

`InputTokensReported` should be within 10% of `TokensAfterCompaction`.

A delta larger than 10% of `TokensAfterCompaction` indicates the internal estimator diverges from reality for that turn.

### 4. MessagesCompacted continuity invariant

`MessagesCompacted` should not decrease between consecutive turns.

A decrease means compaction history is moving backwards, which should not happen.

Also flag any increase of more than 20 messages in a single turn as a discontinuity worth explaining, even if not necessarily a bug.

### 5. Compaction effectiveness invariant

For any turn with `Outcome: Compacted`, `TokensAfterCompaction` must be strictly less than `TokensBeforeCompaction`.

A compaction that does not reduce token count is a no-op and must be flagged.

### 6. Estimator-vs-reported sign invariant

`InputTokensReported` should not be dramatically less than `TokensAfterCompaction`.

If `InputTokensReported` is more than 15% below `TokensAfterCompaction`, that may indicate silent truncation or dropped messages.

## Root-Cause Tracing Requirements

For each violated invariant, inspect the relevant source files and identify the most likely responsible code path.

For each finding, include:

- The likely source class and method
- The expected behavior according to the TokenGuard design
- What the observed behavior implies about the implementation
- A classification:
  - **Bug** — definite incorrect behavior
  - **Suspicious** — possible bug, warrants review
  - **Expected-but-notable** — correct by design, but surprising and worth documenting

## Source Areas to Inspect

Use these as primary reference points when tracing findings:

- `ConversationContext.cs`
  - `PrepareAsync`
  - `_history`
  - token budget logic
  - anchor correction logic
  - emergency truncation logic

- `SlidingWindowStrategy.cs`
  - masking behavior
  - protected window selection
  - `WindowSize`
  - `ProtectedWindowFraction`

- Provider adapter implementations
  - Anthropic adapter
  - OpenAI / OpenRouter adapter
  - source of `InputTokensReported`

- Codexplorer sample agent loop
  - where `PrepareAsync` is called
  - where tool results are appended
  - how model responses are recorded

## Report Format

The generated `audit-<session-timestamp>.md` file must contain:

### 1. Session Summary

Include:

- model
- context budget
- turn count
- session duration
- total savings percentage

### 2. Aggregate Token Economy

Include:

- total `TokensBeforeCompaction`
- total `TokensAfterCompaction`
- total `InputTokensReported`
- absolute savings
- percentage savings
- final-turn savings
- outcome distribution

### 3. Per-Turn Summary Table

Provide one row per turn with all parsed fields.

### 4. Invariant Violations

Add one subsection per violated invariant.

For each violation, list all turns that violated it and include:

- observed values
- expected behavior
- root-cause hypothesis
- severity classification

### 5. Findings Summary

Rank findings by severity:

- Bug
- Suspicious
- Expected-but-notable

## Severity Guidance

Use the following classification rules:

- **Bug**: an invariant is violated in a way that produces incorrect behavior, such as wrong token counts, history silently shrinking, or budget being exceeded without a recovery signal.
- **Suspicious**: values are technically valid but unusual enough to require explanation, such as large estimation gaps or sharp discontinuities.
- **Expected-but-notable**: behavior is correct per the design but may surprise someone reading the metrics for the first time.

## Deliverable Expectations

The report must be:

- accurate
- structured
- reproducible from the transcript and source inspection
- self-contained
- written in clear technical Markdown

## Notes

- Do not skip any invariant.
- Do not silently ignore missing model-response blocks.
- Do not modify the codebase.
- Do not run tests or the application.
- Focus on diagnosis, not repair.