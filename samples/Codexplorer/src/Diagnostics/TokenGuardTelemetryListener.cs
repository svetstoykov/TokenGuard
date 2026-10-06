using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TokenGuard.Core.Diagnostics;

namespace Codexplorer.Diagnostics;

/// <summary>
/// Subscribes Codexplorer to TokenGuard's activity source and meter and writes what they emit to the application log.
/// </summary>
/// <remarks>
/// <para>
/// Codexplorer has no telemetry exporter, so this listener stands in for one: it keeps TokenGuard's tracing and metrics
/// paths active in real sessions and makes their output readable next to the TokenGuard log records.
/// </para>
/// <para>
/// Records are written at <see cref="LogLevel.Debug"/> under the <c>TokenGuard.Telemetry</c> category, so the
/// <c>Logging:LogLevel:TokenGuard</c> setting controls them together with the library's own records.
/// </para>
/// </remarks>
internal sealed class TokenGuardTelemetryListener : IHostedService, IDisposable
{
    private const string LogCategory = "TokenGuard.Telemetry";

    private readonly ILogger _logger;
    private readonly ActivityListener _activityListener;
    private readonly MeterListener _meterListener;

    /// <summary>
    /// Initializes a new instance of the <see cref="TokenGuardTelemetryListener"/> class.
    /// </summary>
    /// <param name="loggerFactory">The factory that creates the logger the telemetry is written to.</param>
    public TokenGuardTelemetryListener(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        this._logger = loggerFactory.CreateLogger(LogCategory);
        this._activityListener = new ActivityListener
        {
            ShouldListenTo = static source => source.Name == TokenGuardDiagnostics.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = this.LogActivity,
        };
        this._meterListener = new MeterListener
        {
            InstrumentPublished = static (instrument, listener) =>
            {
                if (instrument.Meter.Name == TokenGuardDiagnostics.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };
        this._meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => this.LogMeasurement(instrument, value, tags));
        this._meterListener.SetMeasurementEventCallback<int>((instrument, value, tags, _) => this.LogMeasurement(instrument, value, tags));
        this._meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => this.LogMeasurement(instrument, value, tags));
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        ActivitySource.AddActivityListener(this._activityListener);
        this._meterListener.Start();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc/>
    public void Dispose()
    {
        this._activityListener.Dispose();
        this._meterListener.Dispose();
    }

    private static string FormatTags(IEnumerable<KeyValuePair<string, object?>> tags) =>
        string.Join(", ", tags.Select(static tag => $"{tag.Key}={tag.Value}"));

    private void LogActivity(Activity activity)
    {
        if (!this._logger.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        this._logger.LogDebug(
            "Activity {Activity} took {DurationMs:F1} ms with status {Status}: {Tags}",
            activity.OperationName,
            activity.Duration.TotalMilliseconds,
            activity.Status,
            FormatTags(activity.TagObjects));
    }

    private void LogMeasurement(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        if (!this._logger.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        this._logger.LogDebug(
            "Measurement {Instrument} = {Value} {Unit}: {Tags}",
            instrument.Name,
            value,
            instrument.Unit,
            FormatTags(tags.ToArray()));
    }
}
