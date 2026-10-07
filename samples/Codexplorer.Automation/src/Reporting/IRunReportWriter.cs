namespace Codexplorer.Automation.Reporting;

/// <summary>
///     Defines durable finalization of a run report.
/// </summary>
/// <remarks>Report finalization completes independently of the cancelled work token.</remarks>
internal interface IRunReportWriter
{
    /// <summary>
    ///     Asynchronously replaces the run report through a flushed temporary JSON file.
    /// </summary>
    /// <param name="report">The allowlisted report to finalize or validate. Cannot be <see langword="null" />.</param>
    /// <param name="outputDirectory">The destination directory, which is created when absent.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task WriteAsync(RunReport report, string outputDirectory);
}
