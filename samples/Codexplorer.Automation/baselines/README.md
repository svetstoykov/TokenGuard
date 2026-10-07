# Manual run baselines

[`2026-10-07-bat-treatment.json`](2026-10-07-bat-treatment.json) is a real OpenRouter treatment run of the checked-in
[`report-baseline.json`](../src/tasks/report-baseline.json) manifest. It ran from 2026-10-07 14:30:56 UTC to 14:33:57 UTC
on clean commit `e9b1457715a5ceda3bf3cf7e6ea70045d60818e0`, which includes TG-013, the TG-007 implementation, and the
redirected console input fix. The runner exited with code zero.

The single `baseline-bat-docs` task received an in-budget wrap-up reply. Deliverable completion remains `notEvaluated`.
Measurements are complete, the disposal summary cross-check matched, and report validation succeeded.

| Measurement | Value |
| --- | ---: |
| Agent calls / allowance | 28 / 30 |
| Budget overshoot | 0 |
| Provider input / output tokens | 411,204 / 12,767 |
| Completed strategy runs | 24 |
| Summarizer calls | 5 |
| Summarizer input / output tokens | 68,802 / 15,858 |
| Estimator paired turns | 28 |
| Estimated prompt token reduction | 73.6387% |

Agent and summarizer used `deepseek/deepseek-v4.1-flash` with an 8,192 output-token cap. Context settings were
20,000 tokens, soft threshold 0.8, hard threshold 1.0, and protected window 10. Summarization used window 5 and
at least 2,048 tokens of available summary budget and a 4,096-token summary cap. The helper used the same model with
a 512-token cap and temperature zero; no helper calls
were needed. The exchange cap was 50, the task budget 30, and the wrap-up window 4. TokenGuard logging was `Information`.

This is one treatment run. Its estimated reduction comes from before/after prepare counts; a measured provider input
reduction requires a compatible valid control report. No conclusion about equivalent work or answer quality is implied.

Follow the [sample run and comparison instructions](../../Codexplorer/README.md#run-reports-and-comparison) to reproduce
the run, using the effective settings recorded above. The original run used Release executables and absolute paths for
the manifest, TokenGuard checkout, and output directory. It set `Codexplorer__Workspace__RootDirectory` to an empty
`.artifacts/tg007-baseline-workspace` directory. Clear task-owned workspace notes before subsequent treatment/control runs.

The report preserves original machine-local manifest and session-log paths as provenance; those paths may not exist
on another machine. Credentials, provider responses, tool contents, notes, and private logs are excluded from this directory.
The original report passed schema validation and an offline self-comparison with `--limit modelCallsMade=0`.
