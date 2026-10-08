using System.Collections.Concurrent;
using Codexplorer.Agent;
using Codexplorer.Diagnostics;
using Microsoft.Extensions.Logging;
using WorkspaceModel = Codexplorer.Workspace.Workspace;

namespace Codexplorer.Automation;

/// <summary>Owns active automation sessions through their disposal and measurement finalization.</summary>
internal sealed class AutomationSessionRegistry : IAutomationSessionRegistry
{
    private readonly ConcurrentDictionary<string, AutomationSessionRegistration> _sessions = new(StringComparer.Ordinal);
    private readonly ILogger<AutomationSessionRegistry> _logger;
    private readonly ISessionMeasurementCollector? _collector;
    private int _disposed;

    /// <summary>Initializes a new instance of the <see cref="AutomationSessionRegistry" /> class.</summary>
    /// <param name="logger">The registry diagnostic logger.</param>
    /// <param name="collector">The automation collector, or absent for standalone registry use.</param>
    public AutomationSessionRegistry(ILogger<AutomationSessionRegistry> logger, ISessionMeasurementCollector? collector = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        this._logger = logger;
        this._collector = collector;
    }

    /// <inheritdoc />
    public AutomationSessionRegistration Add(WorkspaceModel workspace, IExplorerSession session)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(session);
        ObjectDisposedException.ThrowIf(this._disposed != 0, this);

        while (true)
        {
            var sessionId = $"session_{Guid.NewGuid():N}";
            var registration = new AutomationSessionRegistration(sessionId, workspace, session);

            if (this._sessions.TryAdd(sessionId, registration))
            {
                return registration;
            }
        }
    }

    /// <inheritdoc />
    public bool TryGet(string sessionId, out AutomationSessionRegistration? session)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (this._sessions.TryGetValue(sessionId, out var registration))
        {
            session = registration;
            return true;
        }

        session = null;
        return false;
    }

    /// <inheritdoc />
    public bool TryRemove(string sessionId, out AutomationSessionRegistration? session)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (this._sessions.TryRemove(sessionId, out var registration))
        {
            session = registration;
            return true;
        }

        session = null;
        return false;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref this._disposed, 1) != 0)
        {
            return;
        }

        var disposalCompleted = true;
        foreach (var entry in this._sessions.ToArray())
        {
            if (!this._sessions.TryRemove(entry.Key, out var registration))
            {
                continue;
            }

            try
            {
                await registration.Session.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                disposalCompleted = false;
                this._logger.LogError(ex, "Failed to dispose automation session {SessionId}", registration.SessionId);
            }
        }

        if (disposalCompleted && this._collector?.IsActive == true)
            this._collector.End();
    }
}
