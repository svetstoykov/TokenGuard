using System.Text;
using System.Text.Json;
using Codexplorer.Automation.AgentComparison.Recording;

namespace Codexplorer.Automation.AgentComparison;

/// <summary>Provides readable links from each request to its tool events and cumulative usage.</summary>
internal static class ComparisonDiagnostics
{
    /// <summary>Writes the inspectable timeline and per-task comparison.</summary>
    /// <param name="path">The timeline destination.</param>
    /// <param name="report">The full invocation evidence.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the write.</returns>
    internal static async Task WriteAsync(string path, ComparisonExecutionReport report, CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        text.AppendLine($"# {(report.Synthetic ? "Synthetic offline" : "Captured")} request timeline");
        text.AppendLine();
        text.AppendLine($"Task: {report.Session.Session.Task.TaskId}; arm: {report.Session.Session.Arm}; outcome: {report.Session.Runtime.Outcome}.");
        text.AppendLine($"Evidence checks: {report.Session.EvidenceChecks.Count(check => check.Passed)}/{report.Session.EvidenceChecks.Count}; "
            + $"final-answer checks: {report.Session.FinalAnswerChecks.Count(check => check.Passed)}/{report.Session.FinalAnswerChecks.Count}.");
        text.AppendLine();
        text.AppendLine(report.Usage.CompleteTokens && report.Usage.CompleteCost
            ? "Provider totals are complete for this invocation." : "INCOMPLETE ACCOUNTING: missing usage/cost remains unknown.");
        text.AppendLine("Cached input and reasoning output are subsets; context bytes below are measured sizes, not provider token attribution.");
        text.AppendLine("Unclassified model requests consume the main allowance. Native prompts and tools differ between runtimes.");
        text.AppendLine();
        text.AppendLine("| Attempt | Kind | Status | Request bytes | Input | Output | Cumulative known cost | Capture |");
        text.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- |");
        decimal? known = null;
        var observedReads = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var attempt in report.Attempts)
        {
            if (attempt.Usage?.Cost is { } cost)
                known = (known ?? 0) + cost;
            text.AppendLine($"| {attempt.Sequence} | {attempt.Kind} | {attempt.StatusCode?.ToString() ?? "unknown"} | "
                + $"{attempt.EffectiveBody.Length} | {attempt.Usage?.InputTokens?.ToString() ?? "unknown"} | "
                + $"{attempt.Usage?.OutputTokens?.ToString() ?? "unknown"} | {known?.ToString() ?? "unknown"} | "
                + $"[request](recording/{attempt.CaptureFiles?.EffectiveRequest}) "
                + $"[response](recording/{attempt.CaptureFiles?.Response}) |");
            ObserveReads(attempt.EffectiveBody, observedReads);
        }
        text.AppendLine();
        text.AppendLine("Tool payloads below are observed submitted history, not additional executions:");
        foreach (var attempt in report.Attempts)
        {
            var cachedShare = attempt.Usage is { InputTokens: > 0, CachedInputTokens: not null } usage
                ? (100m * usage.CachedInputTokens.Value / usage.InputTokens.Value).ToString("F2") + "%" : "unknown";
            text.AppendLine($"Attempt {attempt.Sequence}: cached input share={cachedShare}.");
            foreach (var tool in attempt.Tools)
                text.AppendLine($"- `{tool.Name}` / `{tool.CallId}`: arguments={tool.ArgumentBytes} bytes; "
                    + $"result={tool.ResultBytes?.ToString() ?? "unknown"} bytes.");
        }
        text.AppendLine();
        text.AppendLine("Repeated observed read paths:");
        foreach (var group in observedReads.Values.GroupBy(value => value, StringComparer.Ordinal).Where(group => group.Count() > 1))
            text.AppendLine($"- `{group.Key}`: {group.Count()} distinct observed tool-call IDs.");
        text.AppendLine();
        text.AppendLine("Runtime event/capture references (including available compaction events):");
        foreach (var capture in report.Session.Runtime.CapturePaths)
            text.AppendLine($"- [{Path.GetFileName(capture)}]({capture.Replace(Path.DirectorySeparatorChar, '/')})");
        await File.WriteAllTextAsync(path, text.ToString(), cancellationToken).ConfigureAwait(false);
    }

    private static void ObserveReads(byte[] body, Dictionary<string, string> observed)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("messages", out var messages))
                return;
            foreach (var message in messages.EnumerateArray())
            {
                if (!message.TryGetProperty("tool_calls", out var calls))
                    continue;
                foreach (var call in calls.EnumerateArray())
                {
                    var function = call.GetProperty("function");
                    var name = function.GetProperty("name").GetString();
                    if (name is not ("read" or "read_file" or "read_range"))
                        continue;
                    using var arguments = JsonDocument.Parse(function.GetProperty("arguments").GetString()!);
                    foreach (var key in new[] { "path", "filePath" })
                        if (arguments.RootElement.TryGetProperty(key, out var value))
                            observed[call.GetProperty("id").GetString()!] = value.GetString()!;
                }
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            // Malformed native events remain in raw capture; diagnostics cannot establish an observed read from them.
        }
    }
}
