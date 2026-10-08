extern alias sample;

using System.ClientModel;
using System.ClientModel.Primitives;
using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using Serilog;
using TokenGuard.Core;
using TokenGuard.Core.Configuration;
using TokenGuard.Core.Options;
using TokenGuard.Extensions.OpenAI;
using sample::Codexplorer.Diagnostics;

namespace Codexplorer.Automation.Tests;

/// <summary>Owns a real TokenGuard listener and Serilog logger factory for sample tests.</summary>
internal sealed class SampleTelemetryFixture : IDisposable
{
    private readonly Serilog.Core.Logger _serilog;
    private readonly ILoggerFactory _loggerFactory;
    private readonly TokenGuardTelemetryListener _listener;

    /// <summary>Initializes a new instance of the <see cref="SampleTelemetryFixture" /> class.</summary>
    /// <param name="warningLogging">Whether summaries are filtered out.</param>
    public SampleTelemetryFixture(bool warningLogging = false)
    {
        var configuration = new LoggerConfiguration().WriteTo.Sink(new ConversationSummarySink(this.Collector));
        this._serilog = (warningLogging ? configuration.MinimumLevel.Warning() : configuration.MinimumLevel.Information()).CreateLogger();
        this._loggerFactory = LoggerFactory.Create(builder => builder.AddSerilog(this._serilog));
        this._listener = new TokenGuardTelemetryListener(this._loggerFactory, this.Collector);
        this._listener.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>Gets the collector used by the listener and summarizer wrapper.</summary>
    public SessionMeasurementCollector Collector { get; } = new();

    /// <summary>Creates a real conversation context with optional provider-backed summarization.</summary>
    /// <param name="client">The fake provider client, or absent for a large context budget.</param>
    /// <returns>A fresh conversation context.</returns>
    public TokenGuard.Core.Abstractions.IConversationContext CreateContext(ChatClient? client = null)
    {
        var builder = new ConversationConfigBuilder().WithMaxTokens(client is null ? 100_000 : 200).WithLoggerFactory(this._loggerFactory);
        if (client is not null)
            builder.WithSlidingWindowOptions(new SlidingWindowOptions(1)).UseLlmSummarization(
                new MeasuredSummarizerChatClient(client, this.Collector), new LlmSummarizationOptions(1, 10, 50));

        return new ConversationContextFactory(builder.Build()).Create();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this._listener.Dispose();
        this._loggerFactory.Dispose();
        this._serilog.Dispose();
    }
}
