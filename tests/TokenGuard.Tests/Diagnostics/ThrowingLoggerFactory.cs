using Microsoft.Extensions.Logging;

namespace TokenGuard.Tests.Diagnostics;

/// <summary>
///     Represents a logger factory whose loggers throw when a record with one of the given event identifiers is written.
/// </summary>
/// <param name="eventIds">The event identifiers that make a logger throw.</param>
internal sealed class ThrowingLoggerFactory(params int[] eventIds) : ILoggerFactory
{
    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new ThrowingLogger(eventIds);

    /// <inheritdoc />
    public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException();

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private sealed class ThrowingLogger(int[] eventIds) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (eventIds.Contains(eventId.Id))
                throw new InvalidOperationException($"The logger failed on event {eventId.Id}.");
        }
    }
}
