# Codexplorer

Codexplorer is interactive terminal app for exploring GitHub repositories with tool-using LLM agent.

> **TokenGuard at core:** every live agent turn runs through TokenGuard conversation context, calls `PrepareAsync(...)`, compacts old context when thresholds are crossed, and surfaces compaction/degradation state live in terminal.

It is built as standalone sample inside TokenGuard repo and shows off two things at once:

- **Practical repo exploration** in terminal
- **Real TokenGuard-powered context compaction** when conversations get long

## What it does

| Capability | What you get |
| --- | --- |
| Clone repos | Clone GitHub repos from HTTPS or SSH URLs into local workspaces |
| Ask repo questions | Hold multi-turn conversation about one cloned repo |
| Explore with tools | Agent can map tree, list folders, find files, grep, search public web results, read focused file ranges, and fetch readable text from public web pages |
| Create and edit notes | Agent writes notes and deliverables into its session's `artifacts/` folder, outside the cloned repo |
| Edit repo files | Agent can replace exact text in an existing file of the cloned repo when a task asks for a change |
| Stay inside budget | TokenGuard manages session context and compacts message history as token pressure grows |
| See live compaction | Terminal shows prepare results, token counts, compacted messages, degradation warnings, and final answer as run happens |
| Keep transcripts | Every session gets its own directory with a readable markdown transcript, `session.md` |
| Stay safe by default | Agent creates files only in its session's `artifacts/` folder; in the cloned repo it can only edit files that already exist |

## How TokenGuard drives this app

### Setup

Codexplorer wires TokenGuard as real conversation engine at startup:

```csharp
services.AddConversationContext(builder =>
{
    builder
        .WithMaxTokens(budgetOptions.ContextWindowTokens)
        .WithCompactionThreshold(budgetOptions.SoftThresholdRatio)
        .WithEmergencyThreshold(budgetOptions.HardThresholdRatio);
});
```

### Agent flow

Each user message goes through this shape:

| Step | What happens |
| --- | --- |
| 1. User asks question | Message is added into long-lived TokenGuard conversation |
| 2. `PrepareAsync(...)` runs | TokenGuard measures token load and prepares outbound context |
| 3. Compaction may happen | Older messages can be compacted to fit budget |
| 4. State is surfaced live | Terminal shows prepare result, token counts, warnings, degradation |
| 5. Model responds / calls tools | Agent explores repo with `file_tree`, `grep`, `web_search`, `read_range`, `web_fetch`, and more |
| 6. Session continues | Same TokenGuard context carries forward into next turn |

Core prepare-to-LLM step:

```csharp
var prepareResult = await this._conversationContext.PrepareAsync(ct).ConfigureAwait(false);

var completion = (await this._chatClient.CompleteChatAsync(
        prepareResult.Messages.ForOpenAI(),
        ExplorerAgent.CreateChatCompletionOptions(this._chatTools, this._modelOptions.MaxOutputTokens),
        ct)
    .ConfigureAwait(false))
    .Value;
```

TokenGuard prepares compacted outbound message set. Then Codexplorer sends that prepared set to LLM through OpenRouter-backed chat client.

That is main point of sample: **real interactive repo agent running on TokenGuard-managed context and feeding prepared context directly into OpenRouter**, not plain chat history list.

## Quick start

1. Install **.NET 10 SDK**.
2. Copy `samples/Codexplorer/src/appsettings.Development.example.json` to ignored local file `samples/Codexplorer/src/appsettings.Development.json`.
3. Put your own OpenRouter key in that local file, or set `OPENROUTER_API_KEY` in your shell.
4. Optional: add your own Brave Search key in local file or `BRAVE_SEARCH_API_KEY`.
5. Build and run app.
6. Clone repo and start asking questions.

```bash
cd samples/Codexplorer
cp ./src/appsettings.Development.example.json ./src/appsettings.Development.json
dotnet build ./src/Codexplorer.csproj
dotnet run --project ./src/Codexplorer.csproj
```

`src/appsettings.Development.json` is ignored by repo `.gitignore`. Keep your real credentials there locally. Do not put them in committed sample files.

### Automation mode

Run headless stdio host with:

```bash
dotnet run --project ./src/Codexplorer.csproj -- --automation
```

While stdin remains open, automation mode reads one JSON request per line and writes one JSON response per request.
Human logs and warnings stay on stderr so the parent process can parse stdout directly. Closing stdin signals runner
disconnection: it cancels the active command, writes that command's final response, discards queued requests, and disposes
the sessions. A client must keep stdin open until all expected responses arrive, including when sending scripted input.

Minimal request sequence (replace the session ID with the value returned by `open_session`):

```text
{"requestId":"1","command":"ping"}
{"requestId":"2","command":"open_session","payload":{"repositoryUrl":"https://github.com/cli/cli","modelCallBudget":24}}
{"requestId":"3","command":"submit","payload":{"sessionId":"session_id_from_open","message":"Give me the main entry points for this repo."}}
{"requestId":"4","command":"close_session","payload":{"sessionId":"session_id_from_open"}}
```

Responses include `requestId`, `success`, and either `result` or `error`. Ping returns protocol version 1 and an allowlisted
`settings` snapshot. Submit and close return cumulative `measurements`; the final snapshot includes the disposal summary cross-check.

`open_session` accepts `repositoryUrl` for clone-on-open from a public GitHub HTTPS or SSH URL. Automation sessions are sequential; opening a second session fails before another conversation is created. The optional `modelCallBudget` sets the total provider-call allowance for the session.
An optional `wrapUpWindow` reserves calls for the runner wrap-up prompt; it must be positive and smaller than the total allowance.
An optional absolute `sessionDirectory` names the directory the session writes to; without it Codexplorer creates
`<SessionLogsDirectory>/<sessionId>/`, for example `20261008-141502-sharkdp_bat`. An optional `capture` flag (default `false`)
records every model call in the session directory's `capture/` folder. The response returns the `sessionDirectory` in use.
An optional `repositoryCommit` holds a full 40-character commit SHA; the workspace is then checked out at exactly that commit
with its full history, and an existing clone whose head is another commit is deleted and cloned again. An existing clone
already at that commit is restored to the commit's contents before the session starts: edits to tracked files are discarded
and untracked files are removed, so no session sees an earlier session's edits. When the commit cannot
be fetched, `open_session` fails with `clone_failed` and a message naming the repository and the SHA.

If the assistant needs genuine outside clarification from the automation runner, it emits one line that starts exactly with `QUESTION_FOR_RUNNER:`. The `submit` response also surfaces that through `asksRunner` and `runnerQuestion`.

`submit` returns one stable `outcome` value per exchange: `reply_received`, `budget_exceeded`, `max_turns_reached`, `turn_budget_reached`, `empty_model_reply`, `cancelled`, or `failed`. A reply with no text and no tool call is retried once; the provider call that returned it is recorded with status `failed` and its token usage, and a second empty reply in a row ends the exchange as `empty_model_reply` with the session still open. Every response includes the active `sessionId`, `modelTurnsCompleted`, `logFilePath`, whether the session is still open, and any assistant text or partial text that was available for that exchange.

### Automation runner

`Codexplorer.Automation` is a separate executable under `samples/Codexplorer.Automation/src`. It launches Codexplorer with `--automation`, keeps stdout reserved for protocol traffic, logs child stderr separately, and exposes typed `open_session`, `submit`, and `close_session` client calls inside the runner codebase.

First build Codexplorer so automation runner has executable to launch:

```bash
dotnet build ./src/Codexplorer.csproj
```

Then copy `samples/Codexplorer.Automation/src/appsettings.Development.example.json` to ignored local file `samples/Codexplorer.Automation/src/appsettings.Development.json`:

```json
{
  "CodexplorerAutomation": {
    "CodexplorerExecutablePath": "/absolute/path/to/TokenGuard/samples/Codexplorer/src/bin/Debug/net10.0/Codexplorer",
    "ManifestPath": "./tasks/initial-corpus.json",
    "HelperAi": {
      "ModelName": "qwen/qwen3.7-flash",
      "ApiKey": ""
    }
  }
}
```

Set `CodexplorerExecutablePath` to your local absolute path from the previous build. The helper automatically uses
`Codexplorer:OpenRouter:ApiKey` from the sample's `appsettings.Development.json` beside that executable when no helper key
or `OPENROUTER_API_KEY` is configured. Building the sample copies its local development file there, so one local key is enough.
You can also provide separate helper credentials in the runner's ignored local file or through an environment variable:

```bash
export OPENROUTER_API_KEY="your-openrouter-api-key"
```

Then run it from automation project directory:

```bash
cd samples/Codexplorer.Automation/src
cp appsettings.Development.example.json appsettings.Development.json
dotnet run
```

`appsettings.Development.json` is ignored by repo `.gitignore`. Keep your real credentials only in that local file or in environment variables. Do not commit them.

Shipped batch workflow:

1. `samples/Codexplorer.Automation/src/tasks/initial-corpus.json` defines twenty repository-survey tasks.
   `verifiable-corpus.json` in the same directory defines ten pinned tasks (three Small, four Medium, three Large),
   with 31 deliverable checks and four retention probes, including one requiring emergency truncation.
   Select a corpus with `CodexplorerAutomation:ManifestPath`.
2. Runner creates one run folder, loads manifest sequentially, opens one Codexplorer session per task, and continues to next task even when a prior task fails.
3. Each shipped task tells Codexplorer not to modify repository source files and to write its deliverables with the artifact tools.
4. Each task's session writes its notes and deliverables only into its own session directory inside the run folder. A task that
   asks for a change edits files of the cloned repository with `edit_file`; a pinned clone is restored to its commit before the
   next task opens it.

Two runs that use the same workspace root must not run at the same time, because their tasks would edit and restore the same
clone. To run two arms side by side, give each run its own root: set the environment variable
`Codexplorer__Workspace__RootDirectory` to a different absolute path for each runner process. The Codexplorer child process
inherits it.

One run is one self-contained folder, `<OutputDirectory>/<runId>/`. `<runId>` is the UTC start time plus the arm, for example
`20261008-141502-treatment`. The runner fails before running any task when that folder already exists, and logs the run folder
path when it finishes. Session directories are named by `taskId`, so a task has the same folder name in a treatment and a control run.
A `taskId` therefore has to be one directory name: it starts with a letter or digit, contains only letters, digits, `.`, `-`, and `_`,
and is not `run-report.json`. Manifest validation rejects any other value before the run folder is created:

```text
.artifacts/reports/benchmark/
  20261008-141502-treatment/          run folder
    run-report.json                   written by the runner
    <taskId>/                         one session directory per task
      session.md                      markdown transcript, capped for reading
      artifacts/                      the agent's output, empty at start
      capture/                        present when Capture is on
        exchanges.jsonl               one line per model call
        tools.json                    tool schemas, written once
        final-answer.md               complete text of the reply that ended the task
```

`CodexplorerAutomation:OutputDirectory` defaults to `.artifacts/reports/benchmark`. A relative value resolves against the repository
root, found by walking up from the runner's folder to the nearest `.git`; when there is none, against the runner's folder. An absolute
value is used as given. The runner starts Codexplorer with the Codexplorer executable directory as its working directory, and
Codexplorer resolves its relative workspace root from that executable directory.

`CodexplorerAutomation:Capture` defaults to `true`. With capture on, each line of `exchanges.jsonl` holds the model-call number
(the same number as the turn in `session.md`), the complete list of messages sent after compaction, and the complete response:
text, tool calls with full arguments, finish reason, and token usage. Each line is flushed as it is written, so a failed or
cancelled session keeps every call up to that point. Capture is passive: the messages sent, what the agent sees, and every token
count are the same with it on or off. Nothing from the HTTP layer is recorded, so no credential or header reaches a capture file.
`session.md` keeps its length caps and is for reading; `capture/` is the complete record. A Large task can produce several megabytes.

To inspect results after a batch, open the task's session directory in the run folder: `artifacts/` for the agent's notes and
deliverables, `session.md` for the conversation, and `capture/` for exactly what was sent to the model and what came back.

To run a different manifest, point `CodexplorerAutomation:ManifestPath` at another JSON file in `appsettings.Development.json`. The manifest format is:

```json
{
  "tasks": [
    {
      "taskId": "example-task",
      "title": "Example title",
      "repositoryUrl": "https://github.com/cli/cli",
      "taskSize": "Medium",
      "initialPrompt": "Write notes with the artifact tools. Do not modify repository source files, tests, or configuration. Write all task-owned deliverables with the artifact tools."
    }
  ]
}
```

A task may add `"repositoryCommit"` with a full 40-character commit SHA to pin its repository, so the task reads the same file
contents on every run. A malformed SHA fails startup validation with a message naming the task. A pinned clone carries full
history, which counts toward `Workspace:MaxRepoSizeMB`. A reused pinned clone is restored to the pinned commit before the task
starts. A task without `repositoryCommit` clones the default branch and reuses an existing clone as it is, including any edits
an earlier session made in it.

### Deliverable checks and retention probes

Tasks may supply `checks` and a separate `probe`; omission or null means no declaration, and empty checks are allowed.
For example, add this to a task that asks for verified facts without stating their expected answers:

```json
"checks": [
  { "id": "entry-file", "kind": "contains", "anyOf": ["cmd/gh/main.go"], "noneOf": ["cmd/main.go"] },
  { "id": "timeout", "kind": "matches", "artifact": "notes.md", "pattern": "timeout.{0,20}\\b30\\s*(s|seconds)\\b" }
],
"probe": { "canary": "REF-7Q4X-M2", "requires": "dropped" }
```

Check IDs start with an ASCII letter/digit and contain only ASCII letters/digits, dot, hyphen or underscore; they are unique
within a task ignoring case. Kinds are exactly `contains` or `matches`. `contains` requires nonempty `anyOf` and forbids `pattern`;
`matches` requires a non-whitespace pattern and forbids `anyOf`. Either kind can use a nonempty `noneOf` guard. Literal entries
must be nonempty and contain no backticks or asterisks. Null optional fields mean omission. Manifest unknown fields are rejected.
Checks that pass on their own initial prompt are rejected even when targeting artifacts; guards participate in that decision.
A forbidden value cannot match inside a positive alternative.

An omitted/null `artifact` targets the protocol-complete wrap-up answer. An artifact path is relative to the session's `artifacts/`
folder, uses forward slashes and has no leading slash, backslash, colon, empty segment, dot segment or parent segment.
An exact case-sensitive filename wins; otherwise only a unique case-insensitive match is accepted. Missing, ambiguous or unreadable
artifacts fail with `artifactMissing`; an existing empty artifact is still available. Scoring also works with capture disabled.
Failed tasks retain artifact checks, but intermediate replies cannot serve as final answers.

Text and literal values are cleaned in order: remove all backticks/asterisks, replace backslashes with slashes, collapse .NET
whitespace to one space and trim. Matching ignores ordinal case and checks every occurrence. A letter endpoint cannot split a
letter run, and a digit endpoint cannot split a digit run (including BMP Unicode letters/digits). Letter beside digit is allowed.
Thus `Parse` fails on `ParseConfig`, `30` fails on `300` but matches `30s` and `30.5`, and a path may match with a line suffix.
Use alternatives for plurals, rephrased facts, quote styles or Unicode dashes; cleaning performs no stemming or punctuation conversion.
Use a pattern for exact numeric constraints. Patterns run unchanged against cleaned one-line text with forward-slash paths, using
case-insensitive, culture-invariant .NET NonBacktracking and explicit infinite timeout. Unsupported constructs such as lookahead
and backreferences are rejected at startup with the engine explanation.

Check precedence is unavailable text (`noAnswer`/`artifactMissing`), missing positive (`notFound`), forbidden match
(`forbiddenValuePresent`), then pass with null reason. A pattern can match an empty artifact; blank answers remain unavailable.

A canary is 6–32 ASCII letters/digits/hyphens with alphanumeric ends and must not already match the task prompt.
The runner appends its instruction only to the opening submit and asks for it when stopping live work. It removes every normalized
code occurrence from both outgoing helper-model messages, including metadata, questions and repeated replies.
The runner latches code repetition in any original reply before wrap-up; that invalidates the probe even if the final code is present.
`requires` is optional/null or exactly `masked`, `summarized`, or `dropped`. Eligibility requires the greatest-index completed prepare
to show the unchanged full opening user message is absent, then a nonzero counter for any required kind. Event counts alone cannot
prove eligibility. No completed prepare, or surviving/unknown opening evidence, gives `instructionNotCompacted`; a missing kind
gives `requiredKindAbsent`. No answer takes precedence (`noAnswer`), then early repetition (`canaryRepeated`), then eligibility.
An eligible probe passes when the code matches anywhere in the final answer, otherwise fails with `canaryMissing`.
`canaryPresent` remains informational for invalid probes, including full-context controls. Probe pass rate is a regression signal,
not a general claim about instruction retention, and opening survival can make a short run's probe invalid.

The verifiable corpus checks a concrete fact in each final answer and records supporting facts in `evidence.md`.
Its Medium probes request one findings-only progress reply after recording facts so a runner continuation can begin a new
tool loop. The opening user message is protected during its active loop; making that loop longer or lowering its token budget
alone cannot remove that protection. The emergency probe requests an isolated `file_tree` call on `pkg/cmd/api`, then six
separate source ranges in one uninterrupted loop. One tool call per response keeps the newest exchange small enough to fit
the budget while giving truncation older exchanges to drop. After recording its facts, it requests a findings-only progress
reply before further source exploration. Eligibility still depends on the last completed prepare.

Before recording this corpus's baseline, measure the model's guess rate with one no-tools answer per prompt and review real
correct-answer specimens for every check. Freeze the manifest before recording both arms: prompt and check edits change its
SHA-256 and require new compatible reports. Large source reads accumulate in the control arm, so use recorded provider usage
to assess cost rather than assuming each call adds only a few thousand tokens.

### Run reports and comparison

Every manifest run writes UTF-8 schema-version-4 JSON to `<OutputDirectory>/<runId>/run-report.json` using an atomic replacement.
Manifest and checkout identity validation happen before the run folder is created, so a preflight error surfaces its diagnostics
and leaves the output directory untouched.
Run these commands from the TokenGuard repository root after building both sample projects:

```bash
dotnet run --project samples/Codexplorer.Automation/src/Codexplorer.Automation.csproj -- \
  --CodexplorerAutomation:ManifestPath="$PWD/samples/Codexplorer.Automation/src/tasks/report-baseline.json" \
  --CodexplorerAutomation:Arm=treatment
```

Configure the absolute child executable path and provider credentials as described above. The checked-in `report-baseline.json`
contains one documentation survey task for a short manual baseline; `initial-corpus.json` contains twenty repository-survey
tasks, and `verifiable-corpus.json` contains ten pinned, checked tasks. Use the latter path for both arms of a verifiable-corpus baseline.
To identify a checkout when automatic Git discovery cannot find TokenGuard, set `CodexplorerAutomation:RepositoryPath`.
Run metadata records the commit and dirty flag at run start, UTC timestamps, effective model and budget settings, generation caps,
TokenGuard log level, arm, and the SHA-256 hash of the immutable manifest bytes executed. Inline tasks use deterministic JSON
serialization with explicit inline provenance. Reports exclude prompts, answers, tool content, raw configuration, secrets, and exception messages.

Schema version 4 includes these location fields:

| Where | Field | Meaning |
| --- | --- | --- |
| Run | `runId` | Name of the run folder |
| Run | `captureEnabled` | Whether `capture/` folders were written |
| Task | `sessionId` | Codexplorer's session identifier |
| Task | `sessionDirectory` | The task's session directory |
| Task | `artifactsAtStart` | Files in `artifacts/` when the session opened; an isolated session starts with none |
| Task | `artifactsAtEnd` | File paths and sizes in `artifacts/` when the session closed |
| Task | `repositoryCommit` | Optional. The commit SHA the manifest pins the task's repository to; `null` or absent for an unpinned task |

Reports store each check's ID, passed flag and reason, and each probe's required kind, status, reason and `canaryPresent`.
They exclude expected values, regexes and reference codes as well as answer text. All fields are emitted, including empty checks,
null probes and null rates. Completed prepares carry `openingMessagePresent=true/false`; incomplete prepares carry null.
`messagesMasked` and `messagesSummarized` are independent meter counters, alongside `messagesDropped`.
`editCallsSucceeded` counts the `edit_file` calls that changed a repository file and `editCallsFailed` the calls that returned
an error and changed nothing, per task and summed in the totals. A task with both at zero never tried to edit.

| Population | Count/rate |
| --- | --- |
| Started-task checks | `checksTotal`, `checksPassed`, `checkPassRate` = passed / total |
| Started tasks with checks | `evaluatedTaskCount`, `deliverableCompletedTaskCount`, `deliverableCompletionRate` = all-pass / evaluated |
| Declared probes on started tasks | `probeCount`, `invalidProbeCount`, `passedProbeCount`, `probePassRate` = passed / valid |
| Code presence, including invalid probes | `canaryPresentCount` |

Rates are null for empty denominators. Totals sum check populations rather than averaging task rates. Probe results do not affect
deliverable completion. Validation rebuilds counts/rates and checks observable verdict/eligibility invariants; it cannot verify the
truth of text verdicts, early repetition, or manifest completeness without the excluded text.

Every path in the report, including the manifest path, is relative to the run folder, so a run folder can be moved or archived
and a committed report carries no machine-local path. Validation rejects a report that contains an absolute path.

Task turn budgets are hard limits on started agent provider calls, including failed and cancelled attempts. The agent checks the
allowance before preparing another context. Per-exchange caps can pause an exchange but cannot extend the task allowance.
The sample yields a long exchange at the start of the reserved window so the runner can send its wrap-up prompt. An in-budget wrap-up reply counts as protocol completion;
`turn_budget_reached` does not. Deliverable completion is `notEvaluated` without checks, `complete` when every declared check passes,
and `incomplete` otherwise. Check failures and invalid probes measure quality and leave operational exit codes unchanged.
A budget stop returns a nonzero exit code. A run whose tasks all have complete measurements can remain non-partial despite
a budget stop; protocol completion and measurement coverage are separate report fields.

A failed or cancelled run writes a partial report containing completed and active tasks and `unrunTaskIds`. Cancellation reaches
provider and tool work, while transcript and report finalization remain independent of the cancelled work token. If the child
cannot return its final measurements within the cleanup interval, the report retains its last snapshot and marks collection incomplete.
Failed or cancelled tasks, incomplete task measurements, and unrun tasks make the report partial.
Reporting, invalid control measurements, and task failures return a nonzero exit code.

Prepare records include attempted operations that never reached a provider. Token totals count completed prepares. Provider records
retain individual prepare/transcript pairs and call status; the task offset is present only when those pairs agree. Summarizer and helper
usage comes from provider responses, including responses subsequently rejected as empty. Missing usage has explicit nullable totals
and missing-usage-call counts; totals containing missing usage are null. A null value is unavailable, not a measured zero.

Collection runs at every log level. The structured end-of-conversation summary is checked after disposal. `matched`, `mismatched`,
and `unavailable` distinguish a successful cross-check, an invalid collection, and a suppressed or absent summary.
Set `Logging:LogLevel:TokenGuard` to `Information` or lower to retain the summary; Debug output is not required for report collection.

`estimatedPromptTokenReduction = (sumBefore - sumAfter) / sumBefore` describes estimated compaction savings.
Estimator error uses completed prepare/provider pairs with positive reported input usage:
`(providerInput - tokensAfter) / providerInput`. This is a signed fraction (unit `1`): positive values mean the estimate was low,
and negative values mean it was high. Signed and absolute mean/P95 use the same paired-turn population. P95 is nearest-rank.
Zero denominators and empty populations produce null statistics.

For a control run, use a context window large enough to prevent every strategy and summarizer call:

```bash
dotnet run --project samples/Codexplorer.Automation/src/Codexplorer.Automation.csproj -- \
  --CodexplorerAutomation:ManifestPath="$PWD/samples/Codexplorer.Automation/src/tasks/report-baseline.json" \
  --CodexplorerAutomation:Arm=control \
  --CodexplorerAutomation:ControlContextWindowTokens=1000000
```

Each run writes into a new run folder and each task into its own session directory, so one run cannot read another run's notes.
Clones under the workspace root are reused between runs and stay unmodified. Git corpus commits are not pinned here.
A control is invalid if any prepare is incomplete or has an outcome other than `Ready`, or if any strategy or summarizer ran,
even when that strategy changed zero messages.

Comparison runs before host construction and requires neither API credentials nor a configured Codexplorer executable:

```bash
dotnet run --project samples/Codexplorer.Automation/src/Codexplorer.Automation.csproj -- compare \
  .artifacts/reports/benchmark/<control runId>/run-report.json .artifacts/reports/benchmark/<treatment runId>/run-report.json

dotnet run --project samples/Codexplorer.Automation/src/Codexplorer.Automation.csproj -- compare \
  .artifacts/reports/benchmark/<baseline runId>/run-report.json .artifacts/reports/benchmark/<runId>/run-report.json \
  --limit providerInputTokens=1000 --limit estimatorAbsoluteMean=0.05

dotnet run --project samples/Codexplorer.Automation/src/Codexplorer.Automation.csproj -- compare --help
```

The command accepts schema version 4 reports only; reports of an earlier version must be re-recorded, not relabelled. It validates schema and aggregates, then prints baseline, candidate, and delta for totals and matching tasks,
including distributions and unavailable values. It lists tasks present in only one report and categorical completion changes.
It also prints each report's partial flag and unrun task IDs.
By default, models, effective budget/summarization/generation settings, task budgets, and manifest hash must match.
On matching tasks, check-ID sets (ignoring case) and probe presence/required kind must also match. Comparison prints changed
check verdicts/reasons and missing declarations, both probe statuses/reasons/code-presence flags, and failed-side kind counters.
A treatment/control pair may differ only in context-window tokens. `--allow-incompatible` prints mismatches and permits an
informational comparison; an invalid or incomplete control never permits a measured-reduction claim.

For a compatible treatment and valid control with complete provider input usage, comparison prints
`measuredProviderInputReduction = (controlInput - treatmentInput) / controlInput`. This covers every task in both reports and
shows each arm's input tokens, outcomes, and calls, flagging differing outcomes. It is a difference in provider-reported usage;
it does not establish that the arms did equivalent work.

Regression limits use `--limit metric=value` and absolute deltas in the metric's units, not relative percentages.
Limits require non-partial runs, complete task measurements, no unrun tasks, and matching task IDs. Use `--allow-incompatible`
to explicitly permit limits against incomplete or differing coverage; the command prints a coverage warning when doing so.
Comparisons without limits can display differing task coverage without that override.
An increase regresses token/count metrics and absolute estimator error; a decrease regresses protocol completion rate and
estimated reduction. Signed estimator error statistics are informational and cannot have limits. Unknown names, unavailable
metrics, invalid or negative limits, incompatible reports, and exceeded limits return nonzero. Numeric output uses invariant formatting.
Absent known entries in `prepareOutcomeCounts` and `healthSignalCounts` count as zero on both sides, so clean runs accept
limits such as `--limit prepareOutcomeCounts.CannotCompact=0 --limit healthSignalCounts.RepeatedOverBudget=0`.
The known prepare outcomes are `Ready`, `Compacted`, `CompactionInsufficient`, and `CannotCompact`. The known health signals
are `EstimatorDrift`, `RepeatedCompaction`, `LowCompactionYield`, `SummarizationFailureStreak`, `RepeatedOverBudget`,
`PinnedPressure`, and `CheckpointChurn`. Other recorded names remain comparable; absent unknown names are rejected.

Quality limits use absolute metric-unit deltas: decreases regress `checkPassRate`, `deliverableCompletionRate`, and `probePassRate`;
increases regress `invalidProbeCount`, `messagesMasked`, and `messagesSummarized`. Equality and improvements pass; null limited
rates fail. Informational fields cannot have limits: `checksTotal`, `checksPassed`, `evaluatedTaskCount`,
`deliverableCompletedTaskCount`, `probeCount`, `passedProbeCount`, `canaryPresentCount`. There are no default thresholds.
Use `probePassRate` limits between the same arm's valid-probe populations.

Baseline reports are local. A baseline is the `run-report.json` of an earlier run, kept in its run folder under the ignored
`.artifacts/reports/`. Baselines come from real provider calls after committing the implementation; credentials stay in local
configuration or the environment. Deterministic sample tests live in `tests/Codexplorer.Automation.Tests`; live runs remain manual.

## Configuration

### Requirements

- .NET 10 SDK
- OpenRouter API key
- Network access for OpenRouter and GitHub

### Local configuration

Copy `samples/Codexplorer/src/appsettings.Development.example.json` to ignored local file `samples/Codexplorer/src/appsettings.Development.json`, then fill in your own credentials:

```json
{
  "Codexplorer": {
    "OpenRouter": {
      "ApiKey": ""
    },
    "BraveSearch": {
      "ApiKey": ""
    }
  }
}
```

Do not add real keys to committed sample files. Keep them only in ignored `appsettings.Development.json` or in environment variables.

You can also provide the Brave Search key through environment variable instead of configuration:

```bash
export BRAVE_SEARCH_API_KEY="your-brave-search-api-key"
```

If `BRAVE_SEARCH_API_KEY` is missing and `Codexplorer:BraveSearch:ApiKey` is empty, Codexplorer still starts, logs a warning at startup, and `web_search` returns a readable error string when called.

### Optional: override defaults

Shared defaults live in `src/appsettings.json`. You can override any of them in `src/appsettings.Development.json`. A relative `SessionLogsDirectory` resolves against the repository root, found by walking up from Codexplorer's executable directory to the nearest `.git`, or against the executable directory when there is none. Other relative paths, such as the workspace root, resolve from Codexplorer's executable/output directory. Absolute paths are used as given.

Example:

```json
{
  "Codexplorer": {
    "Model": {
      "Name": "qwen/qwen3.7-flash",
      "MaxOutputTokens": 8192,
      "Temperature": 0.0
    },
    "Budget": {
      "ContextWindowTokens": 20000,
      "SoftThresholdRatio": 0.8,
      "HardThresholdRatio": 1.0,
      "WindowSize": 5
    },
    "Workspace": {
      "RootDirectory": "./workspace",
      "CloneDepth": 1,
      "MaxRepoSizeMB": 500
    },
    "Agent": {
      "MaxTurns": 50
    },
    "Logging": {
      "SessionLogsDirectory": ".artifacts/reports/interactive",
      "MinimumLevel": "Information"
    },
    "OpenRouter": {
      "ApiKey": ""
    }
  }
}
```

### TokenGuard logs and telemetry

Codexplorer hands its Serilog logger to TokenGuard through dependency injection, so TokenGuard's records land in the same
console and `logs/codexplorer-*.log` file as the rest of the application. Each record names the conversation ID.

The level is set by the top-level `Logging:LogLevel:TokenGuard` key in `src/appsettings.json` (default `Information`):

```json
{
  "Logging": {
    "LogLevel": {
      "TokenGuard": "Debug"
    }
  }
}
```

At `Debug`, Codexplorer also writes TokenGuard's tracing activities and metric measurements under the
`TokenGuard.Telemetry` category. See [`docs/observability.md`](../../docs/observability.md) for what each level,
event ID, activity, and instrument means.

## Startup guide

Run:

```bash
dotnet run --project ./src/Codexplorer.csproj
```

Main menu gives you four useful paths:

- **Clone a new repo**
- **Query an existing repo**
- **View past session logs**
- **Show current configuration**

## First-run walkthrough

### 1. Clone repo

Pick **Clone a new repo**.

Paste GitHub URL like:

```text
https://github.com/dotnet/runtime
```

or:

```text
git@github.com:dotnet/runtime.git
```

Codexplorer clones repo into local workspace folder. Default workspace root is `./workspace`.

### 2. Ask first question

After clone finishes, app opens query screen.

Good first prompts:

- `Give me high-level architecture of this repo.`
- `Show main entry points and how startup works.`
- `Where is authentication handled?`
- `Find all background job implementations.`
- `Trace request flow from controller to persistence.`
- `What parts look risky or hard to maintain?`

### 3. Watch agent work

During run, terminal shows:

- context preparation result before every model request
- token pressure against configured budget
- how many tokens existed before and after compaction
- whether preparation stayed healthy, became compaction-insufficient, or could not compact further
- tool calls like `file_tree`, `grep`, `web_search`, `read_range`, `web_fetch`, `create_artifact`, and `read_artifact`
- final answer

If TokenGuard has to compact or truncate aggressively, Codexplorer surfaces that in real time instead of hiding it behind silent message dropping.

### 4. Reopen old repos

Back in main menu, choose **Query an existing repo** to continue exploring already-cloned repositories without recloning.

### 5. Review transcript

Choose **View past session logs** to inspect the `session.md` transcript of a previous interactive session.

## Tooling available to agent

Codexplorer is more than chat box. Agent has thirteen tools in three groups.

Repository tools read the cloned repo, and one of them edits it:

| Tool | Use |
| --- | --- |
| `file_tree` | Fast project map |
| `list_directory` | Inspect one folder |
| `find_files` | Match filenames by glob |
| `grep` | Search content with regex |
| `read_file` | Read smaller text files |
| `read_range` | Read exact line windows from larger files |
| `edit_file` | Replace one exact occurrence of `oldText` with `newText` in an existing file |

`edit_file` changes a file only when `oldText` occurs exactly once in it; an absent or repeated text returns an error and leaves
the file as it was. The result names the path and the line range that changed, without the file contents. The tool cannot
create, delete, or rename files, refuses paths under `.git`, and rejects a path that leaves the clone. After an edit, an earlier
read of the file that is still in the conversation describes contents that no longer exist.

Web tools have no root:

| Tool | Use |
| --- | --- |
| `web_search` | Search public web results through Brave Search. Optional `count` defaults to `5`, caps at `10`, and returns compact numbered title/URL/snippet entries |
| `web_fetch` | Fetch readable plain text from one public URL. Optional `max_tokens` defaults to `4000`, caps at `12000`, and appends `[Content truncated at N tokens. Use a smaller range or request a specific section.]` when truncated |

Artifact tools are rooted at the session's `artifacts/` folder:

| Tool | Behaviour |
| --- | --- |
| `create_artifact` | Creates a new UTF-8 text file; fails if it exists, without overwriting it |
| `write_artifact` | Replaces or appends to an existing file; fails if it is missing |
| `read_artifact` | Reads a file, optionally a line range, capped at 2000 lines |
| `list_artifacts` | Lists the files written so far with their sizes |

A path means the same file in all four artifact tools, and tool results report the artifact-relative path, for example `report.md`.
A path that leaves `artifacts/` is rejected. Creating and writing stay separate on purpose: after compaction the agent may not
remember what it wrote, a create on an existing file fails without overwriting it, and the two read tools let it check.

`web_search` is for finding candidate public URLs quickly. Use it to discover promising sources, then call `web_fetch` on the best URLs to read actual page content.

`web_fetch` is for publicly accessible, non-JavaScript-gated pages. It extracts readable text for the model, returns JSON or plain text bodies directly, and reports PDFs or binary responses explicitly instead of dumping raw bytes or raw HTML.

## Where things go

| Path | Purpose |
| --- | --- |
| `./workspace` | Cloned repositories by default, under the executable directory; the agent reads them and edits existing files with `edit_file` |
| `.artifacts/reports/interactive/<sessionId>/` | Session directory of an interactive session: `session.md` and `artifacts/` |
| `.artifacts/reports/benchmark/<runId>/` | Run folder of an automation run: `run-report.json` and one session directory per task |
| `./src/bin/.../logs` | Rolling application logs under build output |

A session writes its transcript, notes and deliverables to exactly one place, its session directory, which is never inside a clone:

```text
<session directory>/
  session.md                 markdown transcript (capped, for reading)
  artifacts/                 the agent's output root, empty at start
  capture/                   automation sessions with capture on
```

`.artifacts/` is gitignored and sits at the repository root. An interactive `<sessionId>` is the UTC start time plus the
repository, for example `20261008-141502-sharkdp_bat`. Interactive sessions have no `capture/` folder.

## Editing scope

Codexplorer agent **creates files** only inside its session's `artifacts/` folder, so its notes never appear in its own repository searches and a deliverable survives a re-clone.

In the cloned repository it **edits existing files** with `edit_file`, one exact text replacement per call. It cannot create, delete, or rename repository files, and it has no shell, so it cannot build or run what it edited. The system prompt tells it to edit only when the task asks for a change.

Edits stay in the local clone; nothing is committed or pushed. A clone pinned to a commit is restored to that commit when the next session opens it. An unpinned clone keeps its edits until it is cloned again.

## Why this sample is interesting

Most repo-chat demos answer one prompt and stop. Codexplorer keeps session alive, lets model use real exploration tools, and uses TokenGuard end-to-end to prepare, compact, and monitor context on every turn. You can actually watch compaction pressure and degradation happen live, then inspect same trail in markdown session logs later.
