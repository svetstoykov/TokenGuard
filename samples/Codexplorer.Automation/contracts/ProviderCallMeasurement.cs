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

    /// <summary>
    ///     Gets the input tokens the provider read from its prompt cache, or <see langword="null" /> when the provider reported none.
    /// </summary>
    /// <remarks>A value of zero is a reported cache miss. The count is part of <see cref="InputTokens" />, not an addition to it.</remarks>
    public long? CachedInputTokens { get; init; } = null;
}
