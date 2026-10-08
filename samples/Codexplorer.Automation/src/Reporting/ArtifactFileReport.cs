namespace Codexplorer.Automation.Reporting;

/// <summary>
///     Represents one file found in a task's artifacts folder.
/// </summary>
internal sealed record ArtifactFileReport
{
    /// <summary>
    ///     Gets the file path relative to the artifacts folder, with forward slashes.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    ///     Gets the file size in bytes.
    /// </summary>
    public required long SizeBytes { get; init; }
}
