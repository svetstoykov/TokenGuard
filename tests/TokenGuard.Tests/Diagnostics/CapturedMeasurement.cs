namespace TokenGuard.Tests.Diagnostics;

/// <summary>
///     Represents one measurement captured by <see cref="TelemetryCapture" />.
/// </summary>
/// <param name="Instrument">The name of the instrument that recorded the measurement.</param>
/// <param name="Unit">The unit of the instrument.</param>
/// <param name="Value">The measured value.</param>
/// <param name="Tags">The tags attached to the measurement.</param>
internal sealed record CapturedMeasurement(string Instrument, string? Unit, double Value, IReadOnlyDictionary<string, object?> Tags)
{
    /// <summary>
    ///     Gets the value of a tag.
    /// </summary>
    /// <param name="name">The tag name.</param>
    /// <returns>The tag value, or <see langword="null" /> when the measurement carries no such tag.</returns>
    public object? Tag(string name) => this.Tags.GetValueOrDefault(name);
}
