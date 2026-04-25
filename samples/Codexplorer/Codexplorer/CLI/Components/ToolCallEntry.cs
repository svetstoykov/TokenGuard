using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Codexplorer.CLI.Components;

internal static class ToolCallEntry
{
    private const int ArgumentPreviewCap = 400;

    public static IRenderable RenderCalled(string toolName, string argumentsJson, CodexplorerTheme theme)
    {
        return new Rows(
            new Text($"Tool call: {toolName}", theme.AccentStyle),
            RenderArguments(argumentsJson),
            new Text($"Argument preview capped at {ArgumentPreviewCap} chars.", theme.MutedStyle));

        IRenderable RenderArguments(string rawJson)
        {
            var preview = Truncate(PrettyPrintJson(rawJson), ArgumentPreviewCap);
            return new Text(preview);
        }
    }

    public static IRenderable RenderCompleted(string toolName, string resultContent, TimeSpan duration, string logFilePath, CodexplorerTheme theme)
    {
        var sizeBytes = System.Text.Encoding.UTF8.GetByteCount(resultContent ?? string.Empty);

        return new Rows(
            new Text($"Tool completed: {toolName}", theme.SuccessStyle),
            new Text($"Result size {sizeBytes:N0} bytes | duration {duration:g}", theme.MutedStyle),
            new Text($"See log file for full output: {logFilePath}", theme.MutedStyle));
    }

    private static string PrettyPrintJson(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return "{}";
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException)
        {
            return rawJson;
        }
    }

    private static string Truncate(string value, int cap)
    {
        if (value.Length <= cap)
        {
            return value;
        }

        return value[..cap] + Environment.NewLine + $"[... truncated: event content exceeded {cap} chars ...]";
    }
}
