# Manual run baselines

The [full pinned-corpus baseline](2026-10-09-verifiable-baseline.md) records ten tasks in both arms:
[control](2026-10-09-verifiable-control.json) and [treatment](2026-10-09-verifiable-treatment.json).
Control passes 31/31 declared checks; treatment passes 21/31 and uses 74.1924% less agent input. Treatment has one
failed task with complete measurements and only one valid retention probe. The notes explain the incomplete
deliverables, formatting requirements, probe denominator, and why the usage difference does not establish equivalent work.
The [calibration evidence](2026-10-09-verifiable-calibration/README.md) preserves real-answer labels, no-tools responses,
probe pilots, and interrupted attempts. The remainder below records the earlier one-task model trial.

[`2026-10-07-bat-treatment.json`](2026-10-07-bat-treatment.json) is the actual schema-3 OpenRouter treatment report for
[`report-baseline.json`](../src/tasks/report-baseline.json). The existing filename now contains the **2026-10-09** run,
replacing the historical schema-2 baseline.

Run `20261009-070245-treatment` started at 2026-10-09 07:02:45.982016 UTC and ended at 07:12:03.943874 UTC on clean
TokenGuard commit `4ccd4f30e1879427ade9a68262abec17f4c8f76b`. The manifest was unpinned; its SHA-256 was
`1a13b81ad88b82243014dc8ab01ca1317ecb37926fca45ab2b95b5fd9eb122a6`.

## Operational gate and settings

The runner exited zero. The single `baseline-bat-docs` task completed its protocol with an in-budget wrap-up reply.
Measurements were complete, the disposal summary cross-check matched, and schema-3 report validation succeeded.
Offline self-comparison with `--limit modelCallsMade=0` exited zero. All six required operational gate conditions held,
so the authorized Qwen default switch was applied; no DeepSeek fallback was run.

Agent, summarizer, and configured helper models were all `qwen/qwen3.7-flash`. Agent/summarizer output cap was 8,192 tokens.
Context settings were 20,000 tokens, soft threshold 0.8, hard threshold 1.0, and protected window 10. Summarization was
enabled with window 5, minimum summary budget 2,048 tokens, and maximum summary output 4,096 tokens. The helper cap was
512 tokens with temperature zero; no helper calls were needed. The exchange cap was 50, task allowance 30 calls, and
wrap-up window four calls. TokenGuard logging was `Information`. The run used rebuilt Release executables and a fresh
workspace under ignored `.artifacts/answer-scorer-workspace.*`.

## Deliverable checks and probe

| Check | Target | Passed | Reason |
| --- | --- | --- | --- |
| `build-manifest` | Final answer | true | null |
| `minimum-rust-setting` | Final answer | true | null |
| `notes-test-command` | `baseline.md` | true | null |

Three of three checks passed: check pass rate **1.0**. One task was evaluated and completed its deliverables:
deliverable completion rate **1.0**. These checks verify the declared names/command under deterministic text matching;
they do not independently certify every factual claim in the generated texts.

The retention probe was **invalid / `canaryRepeated`**, with `canaryPresent=true`. The code appeared in a pre-wrap-up
assistant reply, which takes precedence over compaction eligibility. The last completed prepare showed
`openingMessagePresent=false`; early repetition invalidates the probe even though the final answer includes the code.
There was one probe, one invalid probe, zero passed probes, and one canary-present probe. The valid-probe denominator
was zero, so probe pass rate is **null**.

Both selected specimens were present and nonempty: the final answer was 3,682 UTF-8 bytes and `baseline.md` was 5,650 bytes.
The artifact inventory started empty and ended with `baseline.md`. Both texts were read, screened for credentials/private
paths, and copied without redaction or rewriting into the scorer's recorded fixtures. Six independently reviewed labels
check the manifest, setting name, and test command in each text; provenance and exact byte hashes are in
[ScorerCases.json](../../../tests/Codexplorer.Automation.Tests/Scoring/ScorerCases.json).

## Observed measurements and limitations

| Measurement | Value |
| --- | ---: |
| Agent calls / allowance | 27 / 30 |
| Budget overshoot | 0 |
| Provider input / output tokens | 352,875 / 11,353 |
| Completed strategy runs | 25 |
| Masked / summarized / dropped messages | 220 / 354 / 264 |
| Summarizer calls / failures | 12 / 3 |
| Summarizer input / output tokens | 44,892 / 28,509 |
| Summarization errors | 3 |
| Emergency truncations | 10 |
| Compacted / insufficient / ready prepares | 22 / 3 / 2 |
| Estimator paired turns | 27 |
| Estimated prompt token reduction | 85.1790% |
| Peak prepared tokens | 26,791 |

The report also records five estimator-drift signals, one repeated-compaction signal, and one repeated-over-budget signal.
The operational gate passed despite three summarizer failures and over-budget prepares; those measurements are retained
without adjustment. This single treatment run has no compatible control arm, so estimated before/after prepare reduction
is not a measured provider-input reduction or evidence of general instruction retention. The twenty-task initial corpus
was not run or changed, and the separate unfinished-artifact corpus finding remains open.

## Reproduction and report paths

Follow the [sample run and comparison instructions](../../Codexplorer/README.md#run-reports-and-comparison), using the
effective settings above. The original report is
`.artifacts/reports/benchmark/20261009-070245-treatment/run-report.json`. Its printed run folder has the same run ID.
The report is copied byte-for-byte, preserving its actual provenance, verdicts, manifest hash, and relative paths.

Every path in the report is relative to that original run folder: the manifest path points back into the checkout and the
session directory is the task ID. The surrounding workspace/run folders, transcripts, exchange capture, helper inputs,
credentials, and private logs remain local and ignored. Only the allowlisted report and reviewed public text fixtures
are versioned. The committed baseline also passed offline self-comparison with `--limit modelCallsMade=0`.
