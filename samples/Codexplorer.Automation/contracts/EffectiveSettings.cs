namespace Codexplorer.Measurements;

/// <summary>
///     Represents allowlisted effective sample settings.
/// </summary>
internal sealed record EffectiveSettings
{
    /// <summary>
    ///     Gets the effective agent model slug.
    /// </summary>
    public string AgentModel { get; init; } = "";

    /// <summary>
    ///     Gets the effective summarizer model slug.
    /// </summary>
    public string SummarizerModel { get; init; } = "";

    /// <summary>
    ///     Gets the agent output token cap.
    /// </summary>
    public int MaxOutputTokens { get; init; } = 0;

    /// <summary>
    ///     Gets the effective context window.
    /// </summary>
    public int ContextWindowTokens { get; init; } = 0;

    /// <summary>
    ///     Gets the soft compaction threshold.
    /// </summary>
    public double SoftThresholdRatio { get; init; } = 0;

    /// <summary>
    ///     Gets the emergency threshold.
    /// </summary>
    public double HardThresholdRatio { get; init; } = 0;

    /// <summary>
    ///     Gets the protected sliding window size.
    /// </summary>
    public int WindowSize { get; init; } = 0;

    /// <summary>
    ///     Gets the value indicating whether summarization is enabled.
    /// </summary>
    public bool SummarizationEnabled { get; init; } = false;

    /// <summary>
    ///     Gets the protected summary window size.
    /// </summary>
    public int SummaryWindowSize { get; init; } = 0;

    /// <summary>
    ///     Gets the minimum summary budget.
    /// </summary>
    public int MinSummaryTokens { get; init; } = 0;

    /// <summary>
    ///     Gets the maximum summary budget.
    /// </summary>
    public int MaxSummaryTokens { get; init; } = 0;

    /// <summary>
    ///     Gets the per-exchange model-call cap.
    /// </summary>
    public int ExchangeMaxTurns { get; init; } = 0;

    /// <summary>
    ///     Gets the effective TokenGuard log level.
    /// </summary>
    public string TokenGuardLogLevel { get; init; } = "Information";
}
