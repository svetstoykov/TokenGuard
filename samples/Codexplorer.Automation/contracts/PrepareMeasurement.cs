namespace Codexplorer.Measurements;

/// <summary>
///     Represents the telemetry for one attempted context preparation.
/// </summary>
internal sealed record PrepareMeasurement
{
    /// <summary>
    ///     Gets the one-based prepare record index.
    /// </summary>
    public int Index { get; init; } = 0;

    /// <summary>
    ///     Gets the telemetry conversation identifier.
    /// </summary>
    public string? ConversationId { get; init; } = null;

    /// <summary>
    ///     Gets the TokenGuard turn index.
    /// </summary>
    public int? Turn { get; init; } = null;

    /// <summary>
    ///     Gets the completed or incomplete status.
    /// </summary>
    public string Status { get; init; } = "incomplete";

    /// <summary>
    ///     Gets the completed prepare outcome.
    /// </summary>
    public string? Outcome { get; init; } = null;

    /// <summary>
    ///     Gets the estimated tokens before preparation.
    /// </summary>
    public long? TokensBefore { get; init; } = null;

    /// <summary>
    ///     Gets the estimated prepared tokens.
    /// </summary>
    public long? TokensAfter { get; init; } = null;

    /// <summary>
    ///     Gets the number of messages compacted.
    /// </summary>
    public long MessagesCompacted { get; init; } = 0;

    /// <summary>
    ///     Gets the completed strategy runs belonging to this prepare.
    /// </summary>
    public long StrategyRuns { get; init; } = 0;
}
