namespace Codexplorer.Measurements;

/// <summary>
///     Represents one started provider call and its paired prepare record.
/// </summary>
internal sealed record ProviderCallMeasurement
{
    /// <summary>
    ///     Gets the transcript model-call index.
    /// </summary>
    public int TranscriptIndex { get; init; } = 0;

    /// <summary>
    ///     Gets the paired prepare record index.
    /// </summary>
    public int PrepareIndex { get; init; } = 0;

    /// <summary>
    ///     Gets the completed, failed, or cancelled attempt status.
    /// </summary>
    public string Status { get; init; } = "failed";

    /// <summary>
    ///     Gets the provider-reported input usage.
    /// </summary>
    public long? InputTokens { get; init; } = null;

    /// <summary>
    ///     Gets the provider-reported output usage.
    /// </summary>
    public long? OutputTokens { get; init; } = null;
}
