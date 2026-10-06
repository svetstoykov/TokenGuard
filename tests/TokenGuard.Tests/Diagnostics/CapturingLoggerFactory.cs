using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace TokenGuard.Tests.Diagnostics;

/// <summary>
///     Represents a logger factory that keeps every written record in memory for assertions.
/// </summary>
/// <param name="minimumLevel">The lowest level the created loggers report as enabled.</param>
internal sealed class CapturingLoggerFactory(LogLevel minimumLevel = LogLevel.Trace) : ILoggerFactory
{
    private readonly ConcurrentQueue<CapturedLogRecord> _records = new();
    private readonly AsyncLocal<ScopeNode?> _currentScope = new();
    private readonly LogLevel _minimumLevel = minimumLevel;

    /// <summary>
    ///     Gets the records written so far, in the order they were written.
    /// </summary>
    public IReadOnlyList<CapturedLogRecord> Records => this._records.ToArray();

    /// <summary>
    ///     Gets the records written so far with the given event identifier.
    /// </summary>
    /// <param name="eventId">The event identifier to look for.</param>
    /// <returns>The matching records in the order they were written.</returns>
    public IReadOnlyList<CapturedLogRecord> WithEventId(int eventId) => this._records.Where(record => record.EventId.Id == eventId).ToArray();

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

    /// <inheritdoc />
    public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException();

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private static IReadOnlyList<KeyValuePair<string, object?>> ReadProperties(object? state) =>
        state is IEnumerable<KeyValuePair<string, object?>> pairs ? pairs.ToArray() : [];

    private sealed record ScopeNode(IReadOnlyList<KeyValuePair<string, object?>> Properties, ScopeNode? Parent);

    private sealed class Scope(CapturingLoggerFactory owner, ScopeNode? previous) : IDisposable
    {
        public void Dispose() => owner._currentScope.Value = previous;
    }

    private sealed class CapturingLogger(CapturingLoggerFactory owner, string category) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            var previous = owner._currentScope.Value;
            owner._currentScope.Value = new ScopeNode(ReadProperties(state), previous);
            return new Scope(owner, previous);
        }

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= owner._minimumLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!this.IsEnabled(logLevel))
                return;

            var scope = new List<KeyValuePair<string, object?>>();
            for (var node = owner._currentScope.Value; node is not null; node = node.Parent)
            {
                scope.AddRange(node.Properties);
            }

            owner._records.Enqueue(
                new CapturedLogRecord(category, logLevel, eventId, formatter(state, exception), ReadProperties(state), scope, exception));
        }
    }
}
