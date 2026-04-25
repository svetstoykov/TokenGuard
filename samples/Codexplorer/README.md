# Codexplorer sample

Minimal TokenGuard sample that proves OpenRouter connectivity through `TokenGuard.Extensions.OpenAI`.

## Requirements

- .NET 10 SDK
- local `samples/Codexplorer/Codexplorer.App/appsettings.Development.json`

## Configuration

Default model lives in `samples/Codexplorer/Codexplorer.App/appsettings.json`:

```json
{
  "Codexplorer": {
    "Model": {
      "Name": "google/gemini-2.5-flash"
    }
  }
}
```

Change `Codexplorer:Model:Name` to switch models.

Create local `samples/Codexplorer/Codexplorer.App/appsettings.Development.json` and keep it uncommitted:

```json
{
  "Codexplorer": {
    "OpenRouter": {
      "ApiKey": "your-key"
    }
  }
}
```

Repo `.gitignore` already ignores `appsettings.Development.json`.

## Run

```bash
dotnet run --project samples/Codexplorer/Codexplorer.App
```

App sends one prompt asking model to say hello, prints reply, and exits.
