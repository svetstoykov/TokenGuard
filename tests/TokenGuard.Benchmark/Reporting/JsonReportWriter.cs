using System.Collections;
using System.Reflection;
using System.Text.Json;
using TokenGuard.Benchmark.Models;

namespace TokenGuard.Benchmark.Reporting;

/// <summary>
/// Writes benchmark reports to timestamped JSON files.
/// </summary>
public sealed class JsonReportWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>
    /// Writes report to results directory and returns created file path.
    /// </summary>
    /// <param name="report">Benchmark report to serialize.</param>
    /// <param name="resultsDirectory">Directory that receives timestamped report file.</param>
    /// <returns>Absolute path of written JSON file.</returns>
    public async Task<string> WriteAsync(BenchmarkReport report, string resultsDirectory)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(resultsDirectory);

        Directory.CreateDirectory(resultsDirectory);

        var fileName = $"benchmark-{report.Timestamp:yyyyMMdd-HHmmss}.json";
        var filePath = Path.Combine(resultsDirectory, fileName);
        var json = JsonSerializer.Serialize(report, SerializerOptions);

        await File.WriteAllTextAsync(filePath, json);
        return filePath;
    }

    /// <summary>
    /// Writes failure diagnostics to benchmark workspace and returns user-facing failure message.
    /// </summary>
    /// <param name="taskName">Task name associated with failed run.</param>
    /// <param name="configuration">Configuration associated with failed run.</param>
    /// <param name="model">Model used by failed run.</param>
    /// <param name="workspaceDirectory">Workspace directory used by failed run.</param>
    /// <param name="runId">Unique run identifier for artifact naming.</param>
    /// <param name="completedTurns">Number of completed turns before failure.</param>
    /// <param name="totalInputTokens">Accumulated input tokens before failure.</param>
    /// <param name="totalOutputTokens">Accumulated output tokens before failure.</param>
    /// <param name="compactionEvents">Number of compaction events before failure.</param>
    /// <param name="lastTurn">Last completed turn telemetry if available.</param>
    /// <param name="exception">Exception that terminated benchmark run.</param>
    /// <returns>User-facing failure summary including artifact path.</returns>
    public async Task<string> WriteFailureAsync(
        string taskName,
        BenchmarkConfiguration configuration,
        string model,
        string workspaceDirectory,
        string runId,
        int completedTurns,
        int totalInputTokens,
        int totalOutputTokens,
        int compactionEvents,
        TurnTelemetry? lastTurn,
        Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskName);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(exception);

        var report = new BenchmarkFailureReport(
            RunId: runId,
            TaskName: taskName,
            ConfigurationName: configuration.Name,
            Mode: configuration.Mode.ToString(),
            Model: model,
            WorkspaceDirectory: workspaceDirectory,
            OccurredAtUtc: DateTimeOffset.UtcNow,
            CompletedTurns: completedTurns,
            TotalInputTokens: totalInputTokens,
            TotalOutputTokens: totalOutputTokens,
            CompactionEvents: compactionEvents,
            MaxIterations: configuration.MaxIterations,
            MaxTokens: configuration.MaxTokens,
            CompactionThreshold: configuration.CompactionThreshold,
            LastTurn: lastTurn,
            Exception: CreateFailureException(exception));

        var filePath = await this.WriteFailureReportAsync(report);
        return $"{exception.GetType().Name}: {exception.Message} (details: {filePath})";
    }

    private async Task<string> WriteFailureReportAsync(BenchmarkFailureReport report)
    {
        var artifactDirectory = Path.Combine(report.WorkspaceDirectory, "artifacts");
        Directory.CreateDirectory(artifactDirectory);

        var fileName = $"failure-{SanitizeFileName(report.RunId)}.json";
        var filePath = Path.Combine(artifactDirectory, fileName);
        var json = JsonSerializer.Serialize(report, SerializerOptions);

        await File.WriteAllTextAsync(filePath, json);
        return filePath;
    }

    private static BenchmarkFailureException CreateFailureException(Exception exception)
    {
        var innerException = exception.InnerException is null
            ? null
            : CreateFailureException(exception.InnerException);

        return new BenchmarkFailureException(
            Type: exception.GetType().FullName ?? exception.GetType().Name,
            Message: exception.Message,
            StackTrace: exception.StackTrace,
            Properties: GetExceptionProperties(exception),
            InnerException: innerException);
    }

    private static IReadOnlyDictionary<string, string?> GetExceptionProperties(Exception exception)
    {
        Dictionary<string, string?> properties = new(StringComparer.Ordinal);

        foreach (var property in exception.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            if (property.Name is nameof(Exception.TargetSite)
                or nameof(Exception.Data)
                or nameof(Exception.HelpLink)
                or nameof(Exception.Source)
                or nameof(Exception.InnerException)
                or nameof(Exception.StackTrace)
                or nameof(Exception.Message))
            {
                continue;
            }

            try
            {
                var value = property.GetValue(exception);
                properties[property.Name] = ConvertExceptionPropertyValue(value);
            }
            catch (Exception readException)
            {
                properties[property.Name] = $"<unavailable: {readException.Message}>";
            }
        }

        return properties;
    }

    private static string? ConvertExceptionPropertyValue(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is string text)
        {
            return text;
        }

        if (value is IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
        {
            return string.Join(
                "; ",
                headers.Select(static header => $"{header.Key}={string.Join(",", header.Value)}"));
        }

        if (value is IEnumerable sequence && value is not byte[])
        {
            List<string> items = [];

            foreach (var item in sequence)
            {
                items.Add(item?.ToString() ?? "<null>");
            }

            return string.Join(", ", items);
        }

        return value.ToString();
    }

    private static string SanitizeFileName(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalidCharacters.Contains(character) ? '-' : character).ToArray());
        return sanitized.Replace(' ', '-');
    }
}
