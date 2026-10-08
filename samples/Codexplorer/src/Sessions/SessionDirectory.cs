namespace Codexplorer.Sessions;

/// <summary>
///     Represents the one directory a session writes to.
/// </summary>
/// <remarks>
///     The directory lives outside every cloned repository. It holds the transcript, the agent's artifacts, and, for
///     sessions that capture model calls, the capture files.
/// </remarks>
/// <param name="Path">The absolute path of the session directory.</param>
public sealed record SessionDirectory(string Path)
{
    /// <summary>
    ///     Gets the absolute path of the markdown transcript, <c>session.md</c>.
    /// </summary>
    public string TranscriptPath => System.IO.Path.Combine(this.Path, "session.md");

    /// <summary>
    ///     Gets the absolute path of the <c>artifacts</c> folder that roots the artifact tools.
    /// </summary>
    public string ArtifactsPath => System.IO.Path.Combine(this.Path, "artifacts");

    /// <summary>
    ///     Gets the absolute path of the <c>capture</c> folder that holds the record of every model call.
    /// </summary>
    public string CapturePath => System.IO.Path.Combine(this.Path, "capture");
}
