namespace Codexplorer.Sessions;

/// <summary>
/// Records one Codexplorer query as a durable transcript and exposes the same event stream to runtime consumers.
/// </summary>
/// <remarks>
/// A session logger owns exactly one markdown file. Each appended event must be persisted before control returns so
/// partial transcripts remain inspectable after cancellation, crashes, or abrupt process termination.
/// </remarks>
public interface ISessionLogger : IAsyncDisposable
{
    /// <summary>
    /// Gets the absolute path to the markdown transcript file for this session.
    /// </summary>
    string LogFilePath { get; }

    /// <summary>
    /// Gets an ordered asynchronous stream of session events for downstream renderers.
    /// </summary>
    /// <remarks>
    /// Each enumeration replays prior events first, then yields future events in append order until the session closes.
    /// </remarks>
    IAsyncEnumerable<SessionEvent> Events { get; }

    /// <summary>
    /// Appends one event to the session transcript and publishes it to the in-process event stream.
    /// </summary>
    /// <param name="evt">The event to append.</param>
    /// <param name="ct">The cancellation token for the append operation.</param>
    Task AppendAsync(SessionEvent evt, CancellationToken ct = default);

    /// <summary>
    /// Appends the normal terminal summary for the session and closes the transcript.
    /// </summary>
    /// <param name="summary">The terminal summary event for a successfully completed session.</param>
    /// <param name="ct">The cancellation token for the append operation.</param>
    Task EndAsync(SessionEndedEvent summary, CancellationToken ct = default);
}
