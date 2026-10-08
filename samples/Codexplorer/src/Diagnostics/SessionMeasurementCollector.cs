using System.Diagnostics;
using System.Globalization;
using Codexplorer.Measurements;
using Serilog.Events;

namespace Codexplorer.Diagnostics;

/// <summary>Collects typed telemetry for the single active automation session.</summary>
/// <remarks>This singleton serializes all callbacks and returns independent snapshots.</remarks>
internal sealed class SessionMeasurementCollector : ISessionMeasurementCollector
{
    private readonly object _gate = new();
    private readonly List<PrepareMeasurement> _prepares = [];
    private readonly List<ProviderCallMeasurement> _providers = [];
    private readonly List<UsageMeasurement> _summarizerResponses = [];
    private readonly Dictionary<ActivitySpanId, long> _strategies = [];
    private readonly Dictionary<string, long> _health = new(StringComparer.Ordinal);
    private Dictionary<string, long>? _summary;
    private bool _active;
    private bool _complete;
    private int? _budget;
    private long _summarizerCalls;
    private long _summarizerFailures;
    private long _summarizationErrors;
    private long _dropped;
    private long _emergency;

    /// <inheritdoc />
    public bool IsActive
    {
        get
        {
            lock (this._gate)
            {
                return this._active;
            }
        }
    }

    /// <inheritdoc />
    public void Begin(int? modelCallBudget)
    {
        lock (this._gate)
        {
            if (this._active)
                throw new InvalidOperationException("Only one automation session may be open at a time.");

            this._active = true;
            this._complete = false;
            this._budget = modelCallBudget;
            this._prepares.Clear();
            this._providers.Clear();
            this._summarizerResponses.Clear();
            this._strategies.Clear();
            this._health.Clear();
            this._summary = null;
            this._summarizerCalls = this._summarizerFailures = this._summarizationErrors = this._dropped = this._emergency = 0;
        }
    }

    /// <inheritdoc />
    public void End()
    {
        lock (this._gate)
        {
            this._active = false;
            this._complete = true;
        }
    }

    /// <inheritdoc />
    public SessionMeasurements Snapshot()
    {
        lock (this._gate)
        {
            return new SessionMeasurements
            {
                PrepareRecords = this._prepares.ToArray(),
                ProviderCalls = this._providers.ToArray(),
                SummarizerResponses = this._summarizerResponses.ToArray(),
                SummarizerCalls = this._summarizerCalls,
                SummarizerFailures = this._summarizerFailures,
                SummarizationErrors = this._summarizationErrors,
                MessagesDropped = this._dropped,
                EmergencyTruncations = this._emergency,
                HealthSignalCounts = new Dictionary<string, long>(this._health),
                SummaryCrossCheck = this.CrossCheck(),
                Complete = this._complete,
                ModelCallBudget = this._budget,
            };
        }
    }

    /// <inheritdoc />
    public void ObserveActivity(Activity activity)
    {
        lock (this._gate)
        {
            if (!this._active)
                return;

            if (activity.OperationName == "tokenguard.compact")
            {
                this._strategies[activity.ParentSpanId] = this._strategies.GetValueOrDefault(activity.ParentSpanId) + 1;
            }
            else if (activity.OperationName == "tokenguard.summarize")
            {
                this._summarizerCalls++;
            }
            else if (activity.OperationName == "tokenguard.prepare")
            {
                var outcome = activity.GetTagItem("tokenguard.outcome")?.ToString();
                this._prepares.Add(new PrepareMeasurement
                {
                    Index = this._prepares.Count + 1,
                    ConversationId = activity.GetTagItem("tokenguard.conversation.id")?.ToString(),
                    Turn = (int?)Number(activity.GetTagItem("tokenguard.turn")),
                    Status = outcome is null ? "incomplete" : "completed",
                    Outcome = outcome,
                    TokensBefore = outcome is null ? null : Number(activity.GetTagItem("tokenguard.tokens.before")),
                    TokensAfter = outcome is null ? null : Number(activity.GetTagItem("tokenguard.tokens.after")),
                    MessagesCompacted = outcome is null ? 0 : Number(activity.GetTagItem("tokenguard.messages.compacted")) ?? 0,
                    StrategyRuns = outcome is null ? 0 : this._strategies.GetValueOrDefault(activity.SpanId),
                });
                this._strategies.Remove(activity.SpanId);
            }
        }
    }

    /// <inheritdoc />
    public void ObserveMeasurement(string name, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        lock (this._gate)
        {
            if (!this._active)
                return;

            var count = (long)value;
            if (name == "tokenguard.summarization.failures")
                this._summarizerFailures += count;
            else if (name == "tokenguard.emergency_truncation.count")
                this._emergency += count;
            else if (name == "tokenguard.compaction.messages" && Tag(tags, "tokenguard.kind") == "dropped")
                this._dropped += count;
            else if (name == "tokenguard.health.signals" && Tag(tags, "tokenguard.signal") is { } signal)
                this._health[signal] = this._health.GetValueOrDefault(signal) + count;
        }
    }

    /// <inheritdoc />
    public void ObserveSummary(LogEvent logEvent)
    {
        if (!logEvent.Properties.TryGetValue("EventId", out var eventId)
            || eventId is not StructureValue structure
            || !structure.Properties.Any(property => property.Name == "Id" && property.Value is ScalarValue { Value: 6100 }))
            return;

        lock (this._gate)
        {
            if (!this._active)
                return;

            string[] names = ["PrepareCalls", "StrategyRuns", "TokensReclaimed", "SummarizerCalls", "SummarizerFailures",
                "EmergencyTruncations", "PeakPreparedTokens"];
            this._summary = names.ToDictionary(name => name,
                name => logEvent.Properties.TryGetValue(name, out var value) && value is ScalarValue scalar ? Number(scalar.Value) ?? -1 : -1);
        }
    }

    /// <inheritdoc />
    public void ObservePrepareResult(bool hasSummarizationError)
    {
        lock (this._gate)
        {
            if (this._active && hasSummarizationError)
                this._summarizationErrors++;
        }
    }

    /// <inheritdoc />
    public void ProviderStarted(int transcriptIndex)
    {
        lock (this._gate)
        {
            if (this._active)
                this._providers.Add(new ProviderCallMeasurement
                {
                    TranscriptIndex = transcriptIndex, PrepareIndex = this._prepares.LastOrDefault()?.Index ?? 0, Status = "incomplete",
                });
        }
    }

    /// <inheritdoc />
    public void ProviderFinished(string status, long? inputTokens = null, long? outputTokens = null)
    {
        lock (this._gate)
        {
            if (this._active && this._providers.Count > 0)
                this._providers[^1] = this._providers[^1] with { Status = status, InputTokens = inputTokens, OutputTokens = outputTokens };
        }
    }

    /// <inheritdoc />
    public void SummarizerResponded(long? inputTokens, long? outputTokens)
    {
        lock (this._gate)
        {
            if (this._active)
                this._summarizerResponses.Add(new UsageMeasurement { InputTokens = inputTokens, OutputTokens = outputTokens });
        }
    }

    private string CrossCheck()
    {
        if (!this._complete)
            return "pending";
        if (this._summary is null)
            return "unavailable";

        var runs = this._prepares.Sum(prepare => prepare.StrategyRuns);
        var reclaimed = this._prepares.Where(prepare => prepare.StrategyRuns > 0)
            .Sum(prepare => (prepare.TokensBefore ?? 0) - (prepare.TokensAfter ?? 0));
        var peak = this._prepares.Select(prepare => prepare.TokensAfter ?? 0).DefaultIfEmpty().Max();
        return this._summary["PrepareCalls"] == this._prepares.Count && this._summary["StrategyRuns"] == runs
            && this._summary["TokensReclaimed"] == reclaimed && this._summary["SummarizerCalls"] == this._summarizerCalls
            && this._summary["SummarizerFailures"] == this._summarizerFailures && this._summary["EmergencyTruncations"] == this._emergency
            && this._summary["PeakPreparedTokens"] == peak ? "matched" : "mismatched";
    }

    private static long? Number(object? value) => value is null ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture);

    private static string? Tag(ReadOnlySpan<KeyValuePair<string, object?>> tags, string name)
    {
        foreach (var tag in tags)
        {
            if (tag.Key == name)
                return tag.Value?.ToString();
        }

        return null;
    }
}
