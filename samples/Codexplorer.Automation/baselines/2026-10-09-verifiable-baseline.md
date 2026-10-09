# Verifiable corpus baseline

The full pinned ten-task corpus was run with `qwen/qwen3.7-flash` in both arms. The reports are original runner output,
copied byte-for-byte; no check or probe verdict was changed. See the [calibration evidence](2026-10-09-verifiable-calibration/README.md)
for no-tools observations, all 31 reviewed positive check labels, probe tuning, and the interrupted concurrent attempts.

- [Control report](2026-10-09-verifiable-control.json), SHA-256 `2e3fe5d5daa078349a1afb44d4956c25f35c33978b3fbbfd9438388e9ca90aaa`.
- [Treatment report](2026-10-09-verifiable-treatment.json), SHA-256 `ca78185d4f8d5c270b38faf73b99e5697e764be45262e430c8fefd87dff5bc1a`.

The compatible pair records **74.1924% less provider-reported agent input** in treatment.
This is a difference in measured usage across these runs. Call counts, answers, and outcomes can differ; it does not
establish equivalent work or a general instruction-retention guarantee.

| Provenance | Control | Treatment |
| --- | --- | --- |
| Run ID | `20261009-102737-control` | `20261009-111830-treatment` |
| Started UTC | `2026-10-09T10:27:37.36619+00:00` | `2026-10-09T11:18:30.549778+00:00` |
| Ended UTC | `2026-10-09T11:02:08.603943+00:00` | `2026-10-09T12:19:01.636161+00:00` |
| TokenGuard commit | `289a9a1fc0913ea146c06bc6e99e90cfd2c4f590` | `289a9a1fc0913ea146c06bc6e99e90cfd2c4f590` |
| Dirty at launch | `False` | `False` |

Both arms use manifest SHA-256
`94d7112a080e076f0baaf806b66782b917b1940cb03761300e863ef136defb01`.

## Quality and completion

| Measurement | Control | Treatment |
| --- | ---: | ---: |
| Report marked partial | false | true |
| Evaluated tasks | 10 | 10 |
| Protocol-complete tasks | 10 | 9 |
| All-checks-complete tasks | 10 | 4 |
| Declared probes | 4 | 4 |
| Invalid probes | 4 | 3 |
| Passed valid probes | 0 | 1 |
| Valid-probe pass rate | unavailable | 1 |
| Canary-present probes | 3 | 2 |
| Passed / total checks | 31 / 31 | 21 / 31 |
| Check pass rate | 100.00% | 67.74% |

Protocol completion means an in-budget wrap-up reply; deliverable completion requires every declared check.
The checks verify named paths, symbols, and numeric facts through deterministic text matching. They do not
certify every surrounding claim in the generated prose. Control supplies the observed model/check ceiling
for this single run; the two arms can explore and answer differently.

Treatment is marked partial because of task failure: `rg-preprocessor-retention`.
All ten tasks were evaluated, no tasks are unrun, and measurements and agent input usage are complete.
The comparison without regression limits permits measured input reduction for that complete population.
This does not imply operational success. Failed outcomes and shorter unsuccessful work remain in the comparison.

The preprocessor failure followed five completed provider calls. Its last response had `finishReason=Stop`, no text,
and no tool calls, with 4,591 input and 234 output tokens. The sample raised `ArgumentException` because content must
contain at least one segment. The runner exited one; its report still has complete coverage and measurements.

Numeric checks enforce the requested `label: integer` or `label=integer` form in their declared target. For example,
thread selection reports the correct 12 and 1 in its final Markdown table, but the cap check requires the labeled
colon/equality form, and the sorted-count check targets `evidence.md`. Likewise, the listing answer mentions 1,000,
but its evidence artifact does not satisfy the labeled ceiling check. These scores include deliverable-format and
target failures; they are not a count of factually wrong answers.

| Task | Control checks | Treatment checks | Control outcome | Treatment outcome |
| --- | ---: | ---: | --- | --- |
| `bat-theme-defaults` | 3/3 | 3/3 | `reply_received` | `reply_received` |
| `bat-pager-precedence` | 3/3 | 3/3 | `reply_received` | `reply_received` |
| `bat-asset-loading` | 3/3 | 3/3 | `reply_received` | `reply_received` |
| `rg-thread-selection` | 3/3 | 1/3 | `reply_received` | `reply_received` |
| `rg-encoding-disable` | 3/3 | 1/3 | `reply_received` | `reply_received` |
| `rg-preprocessor-retention` | 3/3 | 0/3 | `reply_received` | `failed` |
| `rg-binary-retention` | 3/3 | 2/3 | `reply_received` | `reply_received` |
| `gh-api-emergency-retention` | 3/3 | 2/3 | `reply_received` | `reply_received` |
| `gh-clone-retention` | 3/3 | 3/3 | `reply_received` | `reply_received` |
| `gh-list-pagination` | 4/4 | 3/4 | `reply_received` | `reply_received` |

## Retention probes

| Task | Control verdict | Treatment verdict | Treatment opening present at last prepare | Required kind |
| --- | --- | --- | --- | --- |
| `rg-preprocessor-retention` | `invalid / instructionNotCompacted` | `invalid / noAnswer` | `true` | `none` |
| `rg-binary-retention` | `invalid / instructionNotCompacted` | `invalid / instructionNotCompacted` | `true` | `none` |
| `gh-api-emergency-retention` | `invalid / canaryRepeated` | `passed` | `false` | `dropped` |
| `gh-clone-retention` | `invalid / canaryRepeated` | `invalid / canaryRepeated` | `false` | `none` |

Only the emergency probe is valid in this full treatment: **one of one valid probes passed**, with three invalid.
It dropped 312 messages through seven emergency truncations, removed the opening message from the last prepared
view, and returned the reference code. Its two-of-three deliverable score is independent of that probe verdict.
The valid-probe rate of 1.0 therefore supplies only one observation. All four probes have separate valid calibration
observations, recorded in the calibration notes; those runs are not pooled into this baseline's denominator.

Probe validity requires the original opening instruction to be absent at the last completed prepare, no early
canary repetition, a final answer, and any declared compaction kind to have occurred. Invalid probes are excluded
from the pass-rate denominator. Full-context controls retain their opening instruction; their probes do not
measure retention after compaction. This small fixed corpus supplies a regression signal, not a general claim.

## Usage and compaction

| Measurement | Control | Treatment |
| --- | ---: | ---: |
| Agent calls | 368 | 341 |
| Budget overshoot | 0 | 0 |
| Provider input tokens | 18,321,331 | 4,728,301 |
| Provider output tokens | 172,637 | 145,933 |
| Missing agent input usage | 0 | 0 |
| Missing agent output usage | 0 | 0 |
| Strategy runs | 0 | 299 |
| Masked messages | 0 | 2,120 |
| Summarized messages | 0 | 7,770 |
| Dropped messages | 0 | 774 |
| Emergency truncations | 0 | 24 |
| Summarizer calls | 0 | 50 |
| Summarizer failures | 0 | 4 |
| Summarizer input tokens | 0 | 291,109 |
| Summarizer output tokens | 0 | 115,399 |
| Missing summarizer input usage | 0 | 0 |
| Missing summarizer output usage | 0 | 0 |
| Helper calls | 0 | 0 |
| Helper input tokens | 0 | 0 |
| Helper output tokens | 0 | 0 |
| Summarization errors | 0 | 4 |
| Peak prepared tokens | 118,011 | 42,287 |
| Estimator absolute mean | 0.022913 | 0.046670 |
| Estimator absolute P95 | 0.097527 | 0.213665 |
| Estimated prepare reduction | 0 | 0.784266 |
| `prepareOutcomeCounts.Compacted` | 0 | 287 |
| `prepareOutcomeCounts.CompactionInsufficient` | 0 | 5 |
| `prepareOutcomeCounts.Ready` | 368 | 49 |
| `healthSignalCounts.EstimatorDrift` | 17 | 37 |
| `healthSignalCounts.LowCompactionYield` | 0 | 6 |
| `healthSignalCounts.RepeatedCompaction` | 0 | 9 |

Measured reduction above uses agent input tokens. Summarizer and helper usage are recorded separately; estimated
before/after prepare reduction is a different metric. Provider price tiers and cache behavior prevent inferring
actual billed cost from one flat input price. No actual-cost claim is made for these runner reports.

## Settings and validation

The only effective-setting difference is context-window size: 1,000,000 control versus 20,000 treatment. Both use
8,192 maximum agent output tokens, soft/hard thresholds 0.8/1.0, protected window 10, summarization window 5,
minimum summary budget 2,048, maximum summary output 4,096, exchange cap 50, and Information TokenGuard logging.
Agent, summarizer, and configured helper use the same fixed Qwen slug; helper cap is 512 with temperature zero.
Small/Medium/Large call allowances are 12/48/64 with wrap-up windows 4/8/8. Capture is enabled. The full-history
repository clones were verified at the pinned commits and are shared read-only; artifacts are isolated per task/run.

Both reports passed schema/aggregate validation and offline self-comparison (control with a zero call-delta
limit, treatment without regression limits). The existing comparison command
accepted the pair without `--allow-incompatible` and printed the measured reduction. All tasks have complete
measurements, no tasks are unrun, and disposal summary cross-checks match. The control recorded no strategy,
summarizer, or compaction activity. Quality failures remain in the reports. The automation suite has 549 passing
tests with zero warnings after calibration.

## Reproduction

Follow the [sample configuration and run instructions](../../Codexplorer/README.md#run-reports-and-comparison).
Build both Release projects, select `verifiable-corpus.json`, and apply the common settings and size budgets above.
Run the arms sequentially; set `Arm=control` with `ControlContextWindowTokens=1000000`, then `Arm=treatment`.
The concurrent attempts and first sequential treatment retry hit provider HTTP 429 and are retained only as
calibration diagnostics. Sequential execution reduces concurrent traffic but does not guarantee provider availability.

From the repository root, these arguments reproduce the recorded manifest, executable, and size budgets:

```bash
Logging__LogLevel__TokenGuard=Information dotnet run -c Release \
  --project samples/Codexplorer.Automation/src/Codexplorer.Automation.csproj -- \
  --CodexplorerAutomation:CodexplorerExecutablePath="$PWD/samples/Codexplorer/src/bin/Release/net10.0/Codexplorer" \
  --CodexplorerAutomation:ManifestPath="$PWD/samples/Codexplorer.Automation/src/tasks/verifiable-corpus.json" \
  --CodexplorerAutomation:Arm=control \
  --CodexplorerAutomation:ControlContextWindowTokens=1000000 \
  --CodexplorerAutomation:TurnBudgets:Small:MaxTurns=12 \
  --CodexplorerAutomation:TurnBudgets:Small:WrapUpWindow=4 \
  --CodexplorerAutomation:TurnBudgets:Medium:MaxTurns=48 \
  --CodexplorerAutomation:TurnBudgets:Medium:WrapUpWindow=8 \
  --CodexplorerAutomation:TurnBudgets:Large:MaxTurns=64 \
  --CodexplorerAutomation:TurnBudgets:Large:WrapUpWindow=8
```

After control completes, use the same command with `Arm=treatment` and omit the control-window override.
The sample's configured treatment window is 20,000. Set the recorded models and summarization settings in local
configuration as documented above. Provider responses are nondeterministic; the report's manifest hash identifies
the exact task bytes and its effective settings identify the actual configuration.

Compare the committed pair without live credentials:

```bash
dotnet run --project samples/Codexplorer.Automation/src/Codexplorer.Automation.csproj -- compare \
  samples/Codexplorer.Automation/baselines/2026-10-09-verifiable-control.json \
  samples/Codexplorer.Automation/baselines/2026-10-09-verifiable-treatment.json
```

Original reports are under ignored `.artifacts/tg008-calibration/full-control-retry/` and
`.artifacts/tg008-calibration/full-treatment-retry-2/`, each in its recorded run-ID folder. Report paths are relative
to those original folders. Transcripts, full captures, credentials, and private logs stay ignored.
