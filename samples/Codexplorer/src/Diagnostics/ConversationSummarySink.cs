using Serilog.Core;
using Serilog.Events;

namespace Codexplorer.Diagnostics;

/// <summary>Forwards structured conversation summaries to the automation collector.</summary>
internal sealed class ConversationSummarySink : ILogEventSink
{
    private readonly ISessionMeasurementCollector _collector;

    /// <summary>Initializes a new instance of the <see cref="ConversationSummarySink" /> class.</summary>
    /// <param name="collector">The singleton session collector.</param>
    public ConversationSummarySink(ISessionMeasurementCollector collector) => this._collector = collector;

    /// <inheritdoc />
    public void Emit(LogEvent logEvent) => this._collector.ObserveSummary(logEvent);
}
