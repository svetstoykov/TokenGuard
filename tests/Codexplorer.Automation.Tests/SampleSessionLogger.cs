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

/// <summary>Persists test session events in memory while checking finalization tokens.</summary>
internal sealed class SampleSessionLogger : ISessionLogger
{
    /// <inheritdoc />
    public string LogFilePath => Path.Combine(Path.GetTempPath(), "sample-test-session.md");

    /// <inheritdoc />
    public IAsyncEnumerable<SessionEvent> Events => Channel.CreateUnbounded<SessionEvent>().Reader.ReadAllAsync();

    /// <inheritdoc />
    public Task AppendAsync(SessionEvent evt, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task EndAsync(SessionEndedEvent summary, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
