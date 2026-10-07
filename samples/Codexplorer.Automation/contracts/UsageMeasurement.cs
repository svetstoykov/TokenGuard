namespace Codexplorer.Measurements;

/// <summary>
///     Represents the token usage reported by one provider response.
/// </summary>
internal sealed record UsageMeasurement
{
    /// <summary>
    ///     Gets the provider-reported input usage.
    /// </summary>
    public long? InputTokens { get; init; } = null;

    /// <summary>
    ///     Gets the provider-reported output usage.
    /// </summary>
    public long? OutputTokens { get; init; } = null;
}
