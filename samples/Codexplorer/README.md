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
        CancellationToken.None)
    .ConfigureAwait(false))
    .Value;
```

TokenGuard prepares compacted outbound message set. Then Codexplorer sends that prepared set to LLM through OpenRouter-backed chat client.

That is main point of sample: **real interactive repo agent running on TokenGuard-managed context and feeding prepared context directly into OpenRouter**, not plain chat history list.

## Quick start

1. Install **.NET 10 SDK**.
2. From `samples/Codexplorer`, create `src/appsettings.Development.json`.
3. Put your OpenRouter key in that file.
4. Run app.
5. Clone repo and start asking questions.

```bash
cd samples/Codexplorer
dotnet build ./Codexplorer.slnx
dotnet run --project ./src/Codexplorer.csproj
```

### Automation mode

Run headless stdio host with:

```bash
dotnet run --project ./src/Codexplorer.csproj -- --automation
```

Automation mode reads exactly one JSON request per stdin line and writes exactly one JSON response per stdout line. Human logs and warnings stay on stderr so parent process can parse stdout directly.

Minimal smoke example:

```text
{"requestId":"1","command":"ping"}
{"requestId":"1","success":true,"result":{"status":"ok","protocolVersion":1},"error":null}
{"requestId":"2","command":"open_session","payload":{"repositoryUrl":"https://github.com/cli/cli"}}
{"requestId":"2","success":true,"result":{"sessionId":"session_0123456789abcdef0123456789abcdef","workspace":{"name":"cli","ownerRepo":"cli/cli","localPath":"/absolute/path/to/workspace/cli-cli","clonedAt":"2026-04-26T09:00:00.0000000Z","sizeBytes":123456789},"logFilePath":"/absolute/path/to/logs/sessions/20260426-090000000-cli-cli-interactive-repo-chat.md"},"error":null}
{"requestId":"3","command":"submit","payload":{"sessionId":"session_0123456789abcdef0123456789abcdef","message":"Give me the main entry points for this repo."}}
{"requestId":"3","success":true,"result":{"sessionId":"session_0123456789abcdef0123456789abcdef","outcome":"reply_received","assistantText":"The main entry points are ...","assistantTextIsPartial":false,"modelTurnsCompleted":2,"reportedTokensConsumed":1874,"sessionOpen":true,"asksRunner":false,"runnerQuestion":null,"logFilePath":"/absolute/path/to/logs/sessions/20260426-090000000-dotnet-runtime-interactive-repo-chat.md","degradationReason":null,"failure":null},"error":null}
```

`open_session` accepts exactly one target:

- `workspacePath` for an already tracked local workspace
- `repositoryUrl` for clone-on-open from a public GitHub HTTPS or SSH URL

If the assistant needs genuine outside clarification from the automation runner, it emits one line that starts exactly with `QUESTION_FOR_RUNNER:`. The `submit` response also surfaces that through `asksRunner` and `runnerQuestion`.

`submit` returns one stable `outcome` value per exchange: `reply_received`, `degraded`, `max_turns_reached`, `cancelled`, or `failed`. Every response includes the active `sessionId`, `modelTurnsCompleted`, `logFilePath`, whether the session is still open, and any assistant text or partial text that was available for that exchange.

### Automation runner

`Codexplorer.Automation` is a separate executable under `samples/Codexplorer.Automation/src`. It launches Codexplorer with `--automation`, keeps stdout reserved for protocol traffic, logs child stderr separately, and exposes typed `open_session`, `submit`, and `close_session` client calls inside the runner codebase.

Create `../Codexplorer.Automation/src/appsettings.Development.json`:

```json
{
  "CodexplorerAutomation": {
    "CodexplorerExecutablePath": "../src/bin/Debug/net10.0/Codexplorer",
    "CodexplorerWorkingDirectory": "..",
    "ManifestPath": "./tasks/initial-corpus.json",
    "HelperAi": {
      "ModelName": "openai/gpt-5.4-mini",
      "ApiKey": "your-openrouter-api-key"
    }
  }
}
```

You can also provide helper credentials through environment variable instead of configuration:

```bash
export OPENROUTER_API_KEY="your-openrouter-api-key"
```

Then run it from automation project directory:

```bash
cd samples/Codexplorer.Automation/src
dotnet run
```

Shipped batch workflow:

1. `samples/Codexplorer.Automation/src/tasks/initial-corpus.json` defines twenty queued tasks with task ID, title, target `workspacePath` or `repositoryUrl`, initial prompt, and size class.
2. Runner loads manifest sequentially, opens one Codexplorer session per task, and continues to next task even when a prior task fails.
3. Each shipped task tells Codexplorer not to modify repository source files and to keep task-owned notes under `.codexplorer/tasks/<task-id>/`.
4. Resulting task artifacts land inside the target workspace under `.codexplorer/tasks/<task-id>/`.
5. Codexplorer session transcripts still land in Codexplorer's normal session log location, which is reported in automation responses and written by Codexplorer itself.

The shipped corpus now mixes direct public GitHub clone targets with normal local-workspace tasks, so custom manifests only need `CODEXPLORER_WORKSPACE_PATH` when they actually use `workspacePath`.

To inspect results after a batch:

- Check `.codexplorer/tasks/` inside the target workspace for task-owned notes and drafts.
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
      "initialPrompt": "Write notes to `.codexplorer/tasks/example-task/report.md`. Do not modify repository source files, tests, or configuration. Keep all task-owned artifacts under `.codexplorer/tasks/example-task/`."
    }
  ]
}
```

`workspacePath` remains supported for already cloned tracked workspaces.

## Configuration

### Requirements

- .NET 10 SDK
- OpenRouter API key
- Network access for OpenRouter and GitHub

### Local configuration

Create `src/appsettings.Development.json`:

```json
{
  "Codexplorer": {
    "OpenRouter": {
      "ApiKey": "your-openrouter-api-key"
    },
    "BraveSearch": {
      "ApiKey": "optional-brave-search-api-key"
    }
  }
}
```

`appsettings.Development.json` is already ignored by repo `.gitignore`, so local secrets stay out of source control.

You can also provide the Brave Search key through environment variable instead of configuration:

```bash
export BRAVE_SEARCH_API_KEY="your-brave-search-api-key"
```

If `BRAVE_SEARCH_API_KEY` is missing and `Codexplorer:BraveSearch:ApiKey` is empty, Codexplorer still starts, logs a warning at startup, and `web_search` returns a readable error string when called.

### Optional: override defaults

Shared defaults live in `src/appsettings.json`. You can override any of them in `src/appsettings.Development.json`.

Example:

```json
{
  "Codexplorer": {
    "Model": {
      "Name": "openai/gpt-5.4-nano",
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
      "ApiKey": "your-openrouter-api-key"
    }
  }
}
```

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
- whether preparation stayed healthy, degraded, or hit context exhaustion
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

Workspace and session-log paths are relative to where you launch app, unless you override them in configuration.

## Editing scope

Codexplorer agent **can create and edit files**, but only inside repo-local `.codexplorer/` scratch directory. It **does not edit repository source files**.

That gives you safe note-taking and intermediate artifacts without mutating checked-in code.

## Why this sample is interesting

Most repo-chat demos answer one prompt and stop. Codexplorer keeps session alive, lets model use real exploration tools, and uses TokenGuard end-to-end to prepare, compact, and monitor context on every turn. You can actually watch compaction pressure and degradation happen live, then inspect same trail in markdown session logs later.
