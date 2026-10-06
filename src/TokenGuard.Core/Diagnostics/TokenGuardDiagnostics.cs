namespace TokenGuard.Core.Diagnostics;

/// <summary>
///     Provides the names that tracing and metrics collectors subscribe to in order to receive TokenGuard telemetry.
/// </summary>
/// <remarks>
///     TokenGuard writes activities and measurements through the diagnostics types built into .NET. Pass these names to an
///     OpenTelemetry-compatible collector, an <see cref="System.Diagnostics.ActivityListener" />, or a
///     <see cref="System.Diagnostics.Metrics.MeterListener" />.
/// </remarks>
/// <example>
///     <code>
///     builder.Services.AddOpenTelemetry()
///         .WithTracing(tracing => tracing.AddSource(TokenGuardDiagnostics.ActivitySourceName))
///         .WithMetrics(metrics => metrics.AddMeter(TokenGuardDiagnostics.MeterName));
///     </code>
/// </example>
public static class TokenGuardDiagnostics
{
    /// <summary>
    ///     The name of the <see cref="System.Diagnostics.ActivitySource" /> that TokenGuard starts its activities on.
    /// </summary>
    public const string ActivitySourceName = "TokenGuard";

    /// <summary>
    ///     The name of the <see cref="System.Diagnostics.Metrics.Meter" /> that TokenGuard creates its instruments on.
    /// </summary>
    public const string MeterName = "TokenGuard";
}
