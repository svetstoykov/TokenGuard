extern alias sample;

using System.Text.Json;
using System.Threading.Channels;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenAI.Chat;
using sample::Codexplorer.Agent;
using sample::Codexplorer.Automation;
using sample::Codexplorer.Configuration;
using sample::Codexplorer.Measurements;
using sample::Codexplorer.Sessions;
using sample::Codexplorer.Tools;
using sample::Codexplorer.Workspace;

namespace Codexplorer.Automation.Tests;

/// <summary>Simulates protocol input EOF while retaining writable terminal output.</summary>
internal sealed class SampleProtocolChannel : IAutomationProtocolChannel
{
    /// <summary>Gets the runner's input queue.</summary>
    public Channel<string> Input { get; } = Channel.CreateUnbounded<string>();

    /// <summary>Gets the sample's response queue.</summary>
    public Channel<AutomationResponseEnvelope> Output { get; } = Channel.CreateUnbounded<AutomationResponseEnvelope>();

    /// <inheritdoc />
    public async ValueTask<string?> ReadLineAsync(CancellationToken ct) =>
        await this.Input.Reader.WaitToReadAsync(ct) ? await this.Input.Reader.ReadAsync(ct) : null;

    /// <inheritdoc />
    public ValueTask WriteResponseAsync(AutomationResponseEnvelope response, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return this.Output.Writer.WriteAsync(response, ct);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
