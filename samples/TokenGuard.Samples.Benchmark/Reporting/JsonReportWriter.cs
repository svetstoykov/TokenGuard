using System.Text.Json;
using TokenGuard.Samples.Benchmark.Models;

namespace TokenGuard.Samples.Benchmark.Reporting;

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
}
