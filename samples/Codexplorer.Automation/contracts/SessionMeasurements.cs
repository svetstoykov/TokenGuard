namespace Codexplorer.Measurements;

/// <summary>
///     Represents a cumulative session measurement snapshot.
/// </summary>
internal sealed record SessionMeasurements
{
    /// <summary>
    ///     Gets the all attempted prepares.
    /// </summary>
    public IReadOnlyList<PrepareMeasurement> PrepareRecords { get; init; } = [];

    /// <summary>
    ///     Gets the all started provider calls.
    /// </summary>
    public IReadOnlyList<ProviderCallMeasurement> ProviderCalls { get; init; } = [];

    /// <summary>
    ///     Gets the received summarizer usage including empty answers.
    /// </summary>
    public IReadOnlyList<UsageMeasurement> SummarizerResponses { get; init; } = [];

    /// <summary>
    ///     Gets the attempted summarizer calls.
    /// </summary>
    public long SummarizerCalls { get; init; } = 0;

    /// <summary>
    ///     Gets the failed summarizer calls excluding caller cancellation.
    /// </summary>
    public long SummarizerFailures { get; init; } = 0;

    /// <summary>
    ///     Gets the prepare results with summarization errors.
    /// </summary>
    public long SummarizationErrors { get; init; } = 0;

    /// <summary>
    ///     Gets the messages dropped according to the compaction meter.
    /// </summary>
    public long MessagesDropped { get; init; } = 0;

    /// <summary>
    ///     Gets the emergency truncation operations.
    /// </summary>
    public long EmergencyTruncations { get; init; } = 0;

    /// <summary>
    ///     Gets the health signal distribution.
    /// </summary>
    public IReadOnlyDictionary<string, long> HealthSignalCounts { get; init; } = new Dictionary<string, long>();

    /// <summary>
    ///     Gets the pending, matched, mismatched, or unavailable summary status.
    /// </summary>
    public string SummaryCrossCheck { get; init; } = "pending";

    /// <summary>
    ///     Gets the value indicating whether collection reached disposal.
    /// </summary>
    public bool Complete { get; init; } = false;

    /// <summary>
    ///     Gets the configured task model-call allowance.
    /// </summary>
    public int? ModelCallBudget { get; init; } = null;
}
