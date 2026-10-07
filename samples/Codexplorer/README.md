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
| Create and edit notes | Agent can create files and update text inside its `.codexplorer/` scratch space |
| Stay inside budget | TokenGuard manages session context and compacts message history as token pressure grows |
| See live compaction | Terminal shows prepare results, token counts, compacted messages, degradation warnings, and final answer as run happens |
| Keep transcripts | Every session is saved as readable markdown log |
| Stay safe by default | Agent scratch writes go only into `.codexplorer/`, not repo source files |

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

Automation mode reads exactly one JSON request per stdin line and writes exactly one JSON response per stdout line. Human logs and warnings stay on stderr so parent process can parse stdout directly.

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

If the assistant needs genuine outside clarification from the automation runner, it emits one line that starts exactly with `QUESTION_FOR_RUNNER:`. The `submit` response also surfaces that through `asksRunner` and `runnerQuestion`.

`submit` returns one stable `outcome` value per exchange: `reply_received`, `budget_exceeded`, `max_turns_reached`, `turn_budget_reached`, `cancelled`, or `failed`. Every response includes the active `sessionId`, `modelTurnsCompleted`, `logFilePath`, whether the session is still open, and any assistant text or partial text that was available for that exchange.

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
      "ModelName": "deepseek/deepseek-v4.1-flash",
      "ApiKey": ""
    }
  }
}
```

Set `CodexplorerExecutablePath` to your local absolute path from previous build. Then provide helper credentials either in that ignored local file or through environment variable:

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

1. `samples/Codexplorer.Automation/src/tasks/initial-corpus.json` defines twenty queued tasks with task ID, title, `repositoryUrl`, initial prompt, and size class.
2. Runner loads manifest sequentially, opens one Codexplorer session per task, and continues to next task even when a prior task fails.
3. Each shipped task tells Codexplorer not to modify repository source files and to keep task-owned notes under an `artifacts/` folder in its current workspace.
4. Resulting task artifacts land inside the target workspace under that `artifacts/` folder.
5. Codexplorer session transcripts still land in Codexplorer's normal session log location, which is reported in automation responses and written by Codexplorer itself.

Automation paths are now resolved relative to each executable's own directory. The runner starts Codexplorer with the Codexplorer executable directory as its working directory, and Codexplorer resolves its relative workspace and log paths from that same executable directory.

To inspect results after a batch:

- Check the `artifacts/` folder inside the target workspace for task-owned notes and drafts.
- Check Codexplorer session transcripts under its configured session logs directory for the full conversation history.

To run a different manifest, point `CodexplorerAutomation:ManifestPath` at another JSON file in `appsettings.Development.json`. The manifest format is:

```json
{
  "tasks": [
    {
      "taskId": "example-task",
      "title": "Example title",
      "repositoryUrl": "https://github.com/cli/cli",
      "taskSize": "Medium",
      "initialPrompt": "Write notes under an `artifacts/` folder in your current workspace. Do not modify repository source files, tests, or configuration. Keep all task-owned artifacts under an `artifacts/` folder in your current workspace."
    }
  ]
}
```

### Run reports and comparison

Every manifest run writes UTF-8 schema-version-1 JSON to `<OutputDirectory>/run-report.json` using an atomic replacement.
Set `CodexplorerAutomation:OutputDirectory` to choose the directory; relative output paths resolve from the runner executable.
Run these commands from the TokenGuard repository root after building both sample projects:

```bash
dotnet run --project samples/Codexplorer.Automation/src/Codexplorer.Automation.csproj -- \
  --CodexplorerAutomation:ManifestPath="$PWD/samples/Codexplorer.Automation/src/tasks/report-baseline.json" \
  --CodexplorerAutomation:OutputDirectory="$PWD/.artifacts/reports/treatment" \
  --CodexplorerAutomation:Arm=treatment
```

Configure the absolute child executable path and provider credentials as described above. The checked-in `report-baseline.json`
contains one documentation survey task for a short manual baseline; `initial-corpus.json` contains the full twenty-task corpus.
To identify a checkout when automatic Git discovery cannot find TokenGuard, set `CodexplorerAutomation:RepositoryPath`.
Run metadata records the commit and dirty flag at run start, UTC timestamps, effective model and budget settings, generation caps,
TokenGuard log level, arm, and the SHA-256 hash of the immutable manifest bytes executed. Inline tasks use deterministic JSON
serialization with explicit inline provenance. Reports exclude prompts, answers, tool content, raw configuration, secrets, and exception messages.

Task turn budgets are hard limits on started agent provider calls, including failed and cancelled attempts. The agent checks the
allowance before preparing another context. Per-exchange caps can pause an exchange but cannot extend the task allowance.
The sample yields a long exchange at the start of the reserved window so the runner can send its wrap-up prompt. An in-budget wrap-up reply counts as protocol completion;
`turn_budget_reached` does not. Deliverable completion is `notEvaluated`: the report does not judge answer quality or artifacts.

A failed or cancelled run writes a partial report containing completed and active tasks and `unrunTaskIds`. Cancellation reaches
provider and tool work, while transcript and report finalization remain independent of the cancelled work token. If the child
cannot return its final measurements within the cleanup interval, the report retains its last snapshot and marks collection incomplete.
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
  --CodexplorerAutomation:OutputDirectory="$PWD/.artifacts/reports/control" \
  --CodexplorerAutomation:Arm=control \
  --CodexplorerAutomation:ControlContextWindowTokens=1000000
```

Before each treatment or control run, manually remove task-owned notes under the cloned repository's `.codexplorer/artifacts/`
(and any `artifacts/` directory created by older runs). Preserve repository source files. Shared workspace notes can change the
agent's behavior and contaminate the comparison; TG-008 will provide isolated workspaces. Git corpus commits are not pinned here.
A control is invalid if any prepare is incomplete or has an outcome other than `Ready`, or if any strategy or summarizer ran,
even when that strategy changed zero messages.

Comparison runs before host construction and requires neither API credentials nor a configured Codexplorer executable:

```bash
dotnet run --project samples/Codexplorer.Automation/src/Codexplorer.Automation.csproj -- compare \
  .artifacts/reports/control/run-report.json .artifacts/reports/treatment/run-report.json

dotnet run --project samples/Codexplorer.Automation/src/Codexplorer.Automation.csproj -- compare \
  .artifacts/reports/treatment/run-report.json .artifacts/reports/candidate/run-report.json \
  --limit providerInputTokens=1000 --limit estimatorAbsoluteMean=0.05

dotnet run --project samples/Codexplorer.Automation/src/Codexplorer.Automation.csproj -- compare --help
```

The command validates schema and aggregates, then prints baseline, candidate, and delta for totals and matching tasks,
including distributions and unavailable values. It lists tasks present in only one report and categorical completion changes.
By default, models, effective budget/summarization/generation settings, task budgets, and manifest hash must match.
A treatment/control pair may differ only in context-window tokens. `--allow-incompatible` prints mismatches and permits an
informational comparison; an invalid or incomplete control never permits a measured-reduction claim.

For a compatible treatment and valid control with complete provider input usage, comparison prints
`measuredProviderInputReduction = (controlInput - treatmentInput) / controlInput`. This covers every task in both reports and
shows each arm's input tokens, outcomes, and calls, flagging differing outcomes. It is a difference in provider-reported usage;
it does not establish that the arms did equivalent work.

Regression limits use `--limit metric=value` and absolute deltas in the metric's units, not relative percentages.
An increase regresses token/count metrics and absolute estimator error; a decrease regresses protocol completion rate and
estimated reduction. Signed estimator error statistics are informational and cannot have limits. Unknown names, unavailable
metrics, invalid or negative limits, incompatible reports, and exceeded limits return nonzero. Numeric output uses invariant formatting.

Manual baseline reports live under `samples/Codexplorer.Automation/baselines/`. Their recorded commit must include both TG-013
and this implementation. Baselines come from real provider calls after committing the implementation; credentials stay in local
configuration or the environment. Deterministic sample tests live in `tests/Codexplorer.Automation.Tests`; live runs remain manual.

The [2026-10-07 treatment baseline](../Codexplorer.Automation/baselines/README.md) records a successful real run of
`report-baseline.json`, including its implementation commit, effective settings, and measurement checks.

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

Shared defaults live in `src/appsettings.json`. You can override any of them in `src/appsettings.Development.json`. Relative paths in those files are resolved from Codexplorer's executable/output directory.

Example:

```json
{
  "Codexplorer": {
    "Model": {
      "Name": "deepseek/deepseek-v4.1-flash",
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
      "SessionLogsDirectory": "./logs/sessions",
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
- tool calls like `file_tree`, `grep`, `web_search`, `read_range`, `web_fetch`, `create_file`, and `write_text`
- final answer

If TokenGuard has to compact or truncate aggressively, Codexplorer surfaces that in real time instead of hiding it behind silent message dropping.

### 4. Reopen old repos

Back in main menu, choose **Query an existing repo** to continue exploring already-cloned repositories without recloning.

### 5. Review transcript

Choose **View past session logs** to inspect markdown transcripts of previous runs.

## Tooling available to agent

Codexplorer is more than chat box. Agent can inspect repo and keep working notes:

| Tool | Use |
| --- | --- |
| `file_tree` | Fast project map |
| `list_directory` | Inspect one folder |
| `find_files` | Match filenames by glob |
| `grep` | Search content with regex |
| `web_search` | Search public web results through Brave Search. Optional `count` defaults to `5`, caps at `10`, and returns compact numbered title/URL/snippet entries |
| `read_file` | Read smaller text files |
| `read_range` | Read exact line windows from larger files |
| `web_fetch` | Fetch readable plain text from one public URL. Optional `max_tokens` defaults to `4000`, caps at `12000`, and appends `[Content truncated at N tokens. Use a smaller range or request a specific section.]` when truncated |
| `create_file` / `write_text` | Create and edit UTF-8 text files under `.codexplorer/` scratch space |

`web_search` is for finding candidate public URLs quickly. Use it to discover promising sources, then call `web_fetch` on the best URLs to read actual page content.

`web_fetch` is for publicly accessible, non-JavaScript-gated pages. It extracts readable text for the model, returns JSON or plain text bodies directly, and reports PDFs or binary responses explicitly instead of dumping raw bytes or raw HTML.

## Where things go

| Path | Purpose |
| --- | --- |
| `./workspace` | Cloned repositories by default |
| `./logs/sessions` | Markdown session transcripts |
| `./src/bin/.../logs` | Rolling application logs under build output |
| `<repo>/.codexplorer/` | Agent-owned scratch notes inside cloned workspace |

Workspace, session-log, and other relative paths are resolved from Codexplorer's executable/output directory, not from your shell's current working directory.

## Editing scope

Codexplorer agent **can create and edit files**, but only inside repo-local `.codexplorer/` scratch directory. It **does not edit repository source files**.

That gives you safe note-taking and intermediate artifacts without mutating checked-in code.

## Why this sample is interesting

Most repo-chat demos answer one prompt and stop. Codexplorer keeps session alive, lets model use real exploration tools, and uses TokenGuard end-to-end to prepare, compact, and monitor context on every turn. You can actually watch compaction pressure and degradation happen live, then inspect same trail in markdown session logs later.
