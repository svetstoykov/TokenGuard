namespace Codexplorer.Automation.AgentComparison.Recording;

/// <summary>Defines the operating mode and optional upstream route for request recording.</summary>
/// <param name="Mode">The fake or future live forwarding mode.</param>
/// <param name="UpstreamEndpoint">The pinned upstream endpoint required only for live mode.</param>
/// <param name="UpstreamCredential">The resolved upstream credential required only for live mode.</param>
/// <param name="FakeProvider">The local scripted provider used only in fake mode.</param>
/// <param name="CaptureDirectory">The directory used to persist request attempts.</param>
/// <param name="Settings">The effective model and pinned provider settings.</param>
/// <param name="UpstreamHandler">An optional isolated transport supplied by deterministic tests.</param>
internal sealed record RecordingProxyOptions(
    RecordingProxyMode Mode,
    Uri? UpstreamEndpoint,
    string? UpstreamCredential,
    FakeOpenRouterProvider? FakeProvider,
    string CaptureDirectory,
    ComparisonSettings Settings,
    HttpMessageHandler? UpstreamHandler = null)
{
    /// <summary>Gets the fresh plan/readiness validation required immediately before forwarding.</summary>
    public Func<CancellationToken, Task<bool>>? AuthorizeForwardAsync { get; init; }
}

/// <summary>Defines whether recorded requests terminate locally or forward to an upstream provider.</summary>
internal enum RecordingProxyMode
{
    /// <summary>Local deterministic provider responses with no public route.</summary>
    Fake,

    /// <summary>Forward requests to the configured upstream endpoint.</summary>
    Live,
}
