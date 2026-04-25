# Codexplorer sample

Minimal TokenGuard sample that proves OpenRouter connectivity through `TokenGuard.Extensions.OpenAI`.

## Requirements

- .NET 10 SDK
- `OPENROUTER_API_KEY` environment variable

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

## Run

```bash
export OPENROUTER_API_KEY=your-key
dotnet run --project samples/Codexplorer/Codexplorer.App
```

App sends one prompt asking model to say hello, prints reply, and exits.
