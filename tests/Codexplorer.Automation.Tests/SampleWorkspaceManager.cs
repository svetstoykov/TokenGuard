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

/// <summary>Provides a reusable in-memory workspace for sample protocol tests.</summary>
internal sealed class SampleWorkspaceManager : IWorkspaceManager
{
    /// <summary>Gets the deterministic workspace.</summary>
    public static Workspace Workspace { get; } = new("b", "a/b", Path.GetTempPath(), DateTime.UnixEpoch, 0);

    /// <inheritdoc />
    public Task<Workspace> CloneAsync(string githubUrl, bool forceReclone = false, CancellationToken ct = default) => Task.FromResult(Workspace);

    /// <inheritdoc />
    public IReadOnlyList<Workspace> ListExisting() => [Workspace];

    /// <inheritdoc />
    public Workspace? Find(string ownerRepo) => Workspace;

    /// <inheritdoc />
    public Workspace? FindByLocalPath(string absoluteLocalPath) => Workspace;
}
