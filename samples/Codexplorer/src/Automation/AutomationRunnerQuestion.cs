using Codexplorer.Agent;

namespace Codexplorer.Automation;

internal static class AutomationRunnerQuestion
{
    public static bool TryExtract(string? assistantText, out string? question)
    {
        question = null;

        if (string.IsNullOrWhiteSpace(assistantText))
        {
            return false;
        }

        foreach (var rawLine in assistantText.Split('\n', StringSplitOptions.TrimEntries))
        {
            var line = rawLine.Trim();

            if (!line.StartsWith(SystemPrompt.RunnerQuestionMarker, StringComparison.Ordinal))
            {
                continue;
            }

            var markerPayload = line[SystemPrompt.RunnerQuestionMarker.Length..].Trim();

            if (string.IsNullOrWhiteSpace(markerPayload))
            {
                return false;
            }

            question = markerPayload;
            return true;
        }

        return false;
    }
}
