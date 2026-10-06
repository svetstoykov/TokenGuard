using Microsoft.Extensions.Logging;

namespace TokenGuard.Tests.Diagnostics;

/// <summary>
///     Represents one log record captured by <see cref="CapturingLoggerFactory" />.
/// </summary>
/// <param name="Category">The logger category that wrote the record.</param>
/// <param name="Level">The level of the record.</param>
/// <param name="EventId">The event identifier of the record.</param>
/// <param name="Message">The formatted message text.</param>
/// <param name="State">The structured properties of the record.</param>
/// <param name="Scope">The structured properties of every scope that was active when the record was written.</param>
/// <param name="Exception">The exception attached to the record, or <see langword="null" /> when none was attached.</param>
internal sealed record CapturedLogRecord(
    string Category,
    LogLevel Level,
    EventId EventId,
    string Message,
    IReadOnlyList<KeyValuePair<string, object?>> State,
    IReadOnlyList<KeyValuePair<string, object?>> Scope,
    Exception? Exception)
{
    /// <summary>
    ///     Gets the value of a structured property, looking at the record state first and the active scopes second.
    /// </summary>
    /// <param name="name">The property name.</param>
    /// <returns>The property value, or <see langword="null" /> when the record carries no such property.</returns>
    public object? Property(string name)
    {
        foreach (var pair in this.State.Concat(this.Scope))
        {
            if (pair.Key == name)
                return pair.Value;
        }

        return null;
    }
}
