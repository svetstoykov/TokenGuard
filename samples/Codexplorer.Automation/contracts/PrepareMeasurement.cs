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

    /// <summary>Gets unseen, visible, masked, or removed for the configured fact; null without a probe or on an incomplete prepare.</summary>
    public string? ProbeValueState { get; init; }

    /// <summary>Gets whether a fact copy contaminates this prepare, or null without probe evidence.</summary>
    public bool? ProbeFactVisibleElsewhere { get; init; }

    /// <summary>Gets whether the wrap-up question has been submitted, or null without probe evidence.</summary>
    public bool? ProbeQuestionAsked { get; init; }

    /// <summary>Gets cumulative post-question refetches through this prepare and its tool turn, or null without probe evidence.</summary>
    public bool? ProbeRefetched { get; init; }

    /// <summary>Gets whether the unchanged opening message survived, or null for an incomplete prepare.</summary>
    public bool? OpeningMessagePresent { get; init; } = null;

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
