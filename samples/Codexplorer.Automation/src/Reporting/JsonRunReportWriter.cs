using System.Text.Json;

namespace Codexplorer.Automation.Reporting;

/// <summary>
///     Represents the UTF-8 JSON run report writer.
/// </summary>
/// <remarks>This stateless service supports singleton registration. Writes to different directories may run concurrently.</remarks>
internal sealed class JsonRunReportWriter : IRunReportWriter
{
    /// <summary>The name of the report file inside the run folder.</summary>
    internal const string FileName = "run-report.json";

    /// <inheritdoc />
    public async Task WriteAsync(RunReport report, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var destination = Path.Combine(outputDirectory, FileName);
        var temporary = Path.Combine(outputDirectory, $".run-report-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, report, ReportJson.Options, CancellationToken.None).ConfigureAwait(false);
                await stream.FlushAsync(CancellationToken.None).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
