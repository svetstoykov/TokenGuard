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
dotnet build ./src/Codexplorer.csproj
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
{"requestId":"2","command":"open_session","payload":{"workspacePath":"/absolute/path/to/workspace/dotnet-runtime"}}
{"requestId":"2","success":true,"result":{"sessionId":"session_0123456789abcdef0123456789abcdef","workspace":{"name":"runtime","ownerRepo":"dotnet/runtime","localPath":"/absolute/path/to/workspace/dotnet-runtime","clonedAt":"2026-04-26T09:00:00.0000000Z","sizeBytes":123456789},"logFilePath":"/absolute/path/to/logs/sessions/20260426-090000000-dotnet-runtime-interactive-repo-chat.md"},"error":null}
```

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
