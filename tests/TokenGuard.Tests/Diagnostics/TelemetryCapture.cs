using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using TokenGuard.Core.Diagnostics;

namespace TokenGuard.Tests.Diagnostics;

/// <summary>
///     Represents a subscription to the TokenGuard activity source and meter that keeps what they emit for assertions.
/// </summary>
internal sealed class TelemetryCapture : IDisposable
{
    private const string ContextNameTag = "tokenguard.context.name";

    private readonly ConcurrentQueue<Activity> _activities = new();
    private readonly ConcurrentQueue<CapturedMeasurement> _measurements = new();
    private readonly ActivityListener _activityListener;
    private readonly MeterListener _meterListener;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TelemetryCapture" /> class and starts listening.
    /// </summary>
    public TelemetryCapture()
    {
        this._activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == TokenGuardDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = this._activities.Enqueue,
        };
        ActivitySource.AddActivityListener(this._activityListener);

        this._meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == TokenGuardDiagnostics.MeterName)
                    listener.EnableMeasurementEvents(instrument);
            },
        };
        this._meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => this.Capture(instrument, value, tags));
        this._meterListener.SetMeasurementEventCallback<int>((instrument, value, tags, _) => this.Capture(instrument, value, tags));
        this._meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => this.Capture(instrument, value, tags));
        this._meterListener.Start();
    }

    /// <summary>
    ///     Gets every activity stopped so far, in the order they stopped.
    /// </summary>
    public IReadOnlyList<Activity> Activities => this._activities.ToArray();

    /// <summary>
    ///     Gets every measurement recorded so far, in the order they were recorded.
    /// </summary>
    public IReadOnlyList<CapturedMeasurement> Measurements => this._measurements.ToArray();

    /// <summary>
    ///     Gets the stopped activities with the given name.
    /// </summary>
    /// <param name="name">The activity name.</param>
    /// <returns>The matching activities in the order they stopped.</returns>
    public IReadOnlyList<Activity> ActivitiesNamed(string name) => this._activities.Where(activity => activity.OperationName == name).ToArray();

    /// <summary>
    ///     Gets the measurements recorded by one instrument for one context name.
    /// </summary>
    /// <param name="instrument">The instrument name.</param>
    /// <param name="contextName">The context name the measurements must be tagged with.</param>
    /// <returns>The matching measurements in the order they were recorded.</returns>
    public IReadOnlyList<CapturedMeasurement> MeasurementsOf(string instrument, string contextName) =>
        this._measurements.Where(m => m.Instrument == instrument && contextName.Equals(m.Tag(ContextNameTag))).ToArray();

    /// <inheritdoc />
    public void Dispose()
    {
        this._activityListener.Dispose();
        this._meterListener.Dispose();
    }

    private void Capture(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags) =>
        this._measurements.Enqueue(new CapturedMeasurement(instrument.Name, instrument.Unit, value, tags.ToArray().ToDictionary()));
}
