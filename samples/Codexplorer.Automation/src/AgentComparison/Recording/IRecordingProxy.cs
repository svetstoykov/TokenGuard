namespace Codexplorer.Automation.AgentComparison.Recording;

/// <summary>Provides a loopback provider endpoint that captures every model attempt.</summary>
/// <remarks>The implementation owns its listener and must be disposed after all scoped children exit.</remarks>
internal interface IRecordingProxy : IAsyncDisposable
{
    /// <summary>Gets the local OpenAI-compatible endpoint exposed to child runtimes.</summary>
    Uri Endpoint { get; }

    /// <summary>Gets an immutable snapshot of captured attempts.</summary>
    IReadOnlyList<RecordedAttempt> Attempts { get; }

    /// <summary>Gets the safe capture-persistence failure type, or null when capture remains complete.</summary>
    string? CaptureFailure { get; }

    /// <summary>Asynchronously starts the loopback listener.</summary>
    /// <param name="cancellationToken">A token to observe while starting the listener.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    ValueTask StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Waits for every accepted request handler to finish capture and persistence.</summary>
    /// <param name="cancellationToken">A token to observe while waiting for active handlers.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    ValueTask DrainAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates a child credential and budget scope for one session.</summary>
    /// <param name="options">The session identity, call limits, and child credential.</param>
    /// <returns>The active scope bound to incoming requests using its credential.</returns>
    RecordingScope BeginScope(RecordingScopeOptions options);
}
