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

/// <summary>Provides a controllable workspace tool boundary.</summary>
internal sealed class SampleToolRegistry : IToolRegistry
{
    private readonly bool _waitForCancellation;

    /// <summary>Initializes a new instance of the <see cref="SampleToolRegistry" /> class.</summary>
    /// <param name="waitForCancellation">Whether a tool waits for its work token to be cancelled.</param>
    public SampleToolRegistry(bool waitForCancellation = false) => this._waitForCancellation = waitForCancellation;
    /// <summary>Gets the signal that tool execution has started.</summary>
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <inheritdoc />
    public IReadOnlyList<ToolSchema> GetSchemas() => [];

    /// <inheritdoc />
    public async Task<string> ExecuteAsync(string toolName, JsonElement arguments, Workspace workspace, CancellationToken ct)
    {
        this.Started.TrySetResult();
        if (this._waitForCancellation)
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        return "result";
    }
}
