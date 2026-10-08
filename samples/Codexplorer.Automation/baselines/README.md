# Manual run baselines

[`2026-10-07-bat-treatment.json`](2026-10-07-bat-treatment.json) is a real OpenRouter treatment run of the checked-in
[`report-baseline.json`](../src/tasks/report-baseline.json) manifest, recorded as report schema version 2. It replaces the
schema version 1 report of 2026-10-07 under the same file name. The run, `20261008-123200-treatment`, ran from
2026-10-08 12:32:00 UTC to 12:34:50 UTC on clean commit `6345681f8d194b5a6200a302f738a3d90af6c478`, which includes the
session directory, the artifact tools, model-call capture, and report schema version 2. The runner exited with code zero.

The single `baseline-bat-docs` task received an in-budget wrap-up reply. Deliverable completion remains `notEvaluated`.
Measurements are complete, the disposal summary cross-check matched, and report validation succeeded.

| Measurement | Value |
| --- | ---: |
| Agent calls / allowance | 30 / 30 |
| Budget overshoot | 0 |
| Provider input / output tokens | 465,437 / 9,350 |
| Completed strategy runs | 28 |
| Summarizer calls | 3 |
| Summarizer input / output tokens | 29,369 / 5,400 |
| Estimator paired turns | 30 |
| Estimated prompt token reduction | 67.8290% |

Agent and summarizer used `deepseek/deepseek-v4.1-flash` with an 8,192 output-token cap. Context settings were
20,000 tokens, soft threshold 0.8, hard threshold 1.0, and protected window 10. Summarization used window 5 and
at least 2,048 tokens of available summary budget and a 4,096-token summary cap. The helper used the same model with
a 512-token cap and temperature zero; no helper calls
were needed. The exchange cap was 50, the task budget 30, and the wrap-up window 4. TokenGuard logging was `Information`.
These are the settings of the 2026-10-07 run. The manifest hash differs because the task prompt now names the artifact tools.

What the run showed about the session directory:

| Check | Result |
| --- | --- |
| The clone contains no `.codexplorer/` folder | Held |
| The agent's file is in the task's `artifacts/` folder | `baseline.md`, 8,338 bytes; `artifactsAtStart` is empty |
| `capture/exchanges.jsonl` has one line per agent call | 30 lines for 30 calls |
| The agent found what it wrote under the same path | It created `baseline.md` at call 27 and `list_artifacts` returned it at call 28. It did not call `read_artifact` |
| The report validates as schema version 2 and contains no absolute path | Held |

This is one treatment run. Its estimated reduction comes from before/after prepare counts; a measured provider input
reduction requires a compatible valid control report. No conclusion about equivalent work or answer quality is implied.

Follow the [sample run and comparison instructions](../../Codexplorer/README.md#run-reports-and-comparison) to reproduce
the run, using the effective settings recorded above. The original run used Release executables, an absolute manifest path,
and the default output directory, so its run folder was `.artifacts/reports/benchmark/20261008-123200-treatment`. It set
`Codexplorer__Workspace__RootDirectory` to an empty `.artifacts/tg007b-baseline-workspace` directory.

Every path in the report is relative to that run folder: the manifest path points back into the checkout and the session
directory is the task ID. The run folder itself, with its transcript, artifacts, and capture files, stays local and is not
part of this directory. Credentials, provider responses, tool contents, notes, and private logs are excluded from this directory.
The report passed schema validation and an offline self-comparison with `--limit modelCallsMade=0`.
