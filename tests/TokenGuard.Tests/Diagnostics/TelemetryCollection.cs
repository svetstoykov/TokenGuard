namespace TokenGuard.Tests.Diagnostics;

/// <summary>
///     Defines the collection for tests that subscribe to the process-wide TokenGuard activity source and meter.
/// </summary>
/// <remarks>
///     The source and meter are static, so a listener in one test would otherwise change what a concurrently running test
///     observes. Tests in this collection run one at a time and never alongside the rest of the suite.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TelemetryCollection
{
    /// <summary>
    ///     The name of the collection.
    /// </summary>
    public const string Name = "Telemetry";
}
