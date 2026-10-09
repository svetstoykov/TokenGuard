using System.Diagnostics;
using Codexplorer.Measurements;
using Serilog.Events;

namespace Codexplorer.Diagnostics;

/// <summary>Defines collection of one automation session's measurements.</summary>
internal interface ISessionMeasurementCollector
{
    /// <summary>Gets whether an automation session is being collected.</summary>
    bool IsActive { get; }

    /// <summary>Begins collection before the conversation is created.</summary>
    /// <param name="modelCallBudget">The optional total provider-call allowance.</param>
    /// <exception cref="InvalidOperationException">Another session is active.</exception>
    void Begin(int? modelCallBudget);

    /// <summary>Ends collection after the conversation is disposed.</summary>
    void End();

    /// <summary>Copies the cumulative measurements.</summary>
    /// <returns>An immutable copy of the current measurements.</returns>
    SessionMeasurements Snapshot();

    /// <summary>Records one stopped TokenGuard activity.</summary>
    /// <param name="activity">The stopped activity.</param>
    void ObserveActivity(Activity activity);

    /// <summary>Records one allowlisted meter measurement.</summary>
    /// <param name="name">The instrument name.</param>
    /// <param name="value">The measurement value.</param>
    /// <param name="tags">The instrument tags.</param>
    void ObserveMeasurement(string name, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags);

    /// <summary>Records the structured conversation summary event.</summary>
    /// <param name="logEvent">The event emitted by Serilog.</param>
    void ObserveSummary(LogEvent logEvent);

    /// <summary>Records opening-message evidence and any summarization error from a completed prepare.</summary>
    /// <param name="hasSummarizationError">Whether the prepare reported a summarization error.</param>
    /// <param name="openingMessagePresent">Whether the unchanged opening user message remains in the prepared context.</param>
    void ObservePrepareResult(bool hasSummarizationError, bool openingMessagePresent);

    /// <summary>Records the beginning of a provider attempt.</summary>
    /// <param name="transcriptIndex">The transcript index of the attempt.</param>
    void ProviderStarted(int transcriptIndex);

    /// <summary>Records the final status and usage of a provider attempt.</summary>
    /// <param name="status">The completed, failed, or cancelled status.</param>
    /// <param name="inputTokens">Provider input usage, or absent when unavailable.</param>
    /// <param name="outputTokens">Provider output usage, or absent when unavailable.</param>
    void ProviderFinished(string status, long? inputTokens = null, long? outputTokens = null);

    /// <summary>Records a received summarizer response, including an empty answer.</summary>
    /// <param name="inputTokens">Provider input usage, or absent when unavailable.</param>
    /// <param name="outputTokens">Provider output usage, or absent when unavailable.</param>
    void SummarizerResponded(long? inputTokens, long? outputTokens);
}
