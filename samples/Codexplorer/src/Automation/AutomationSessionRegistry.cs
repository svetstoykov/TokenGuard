using System.Collections.Concurrent;
using Codexplorer.Agent;
using Microsoft.Extensions.Logging;
using WorkspaceModel = Codexplorer.Workspace.Workspace;

namespace Codexplorer.Automation;

internal sealed class AutomationSessionRegistry : IAutomationSessionRegistry
{
    private readonly ConcurrentDictionary<string, AutomationSessionRegistration> _sessions = new(StringComparer.Ordinal);
    private readonly ILogger<AutomationSessionRegistry> _logger;
    private int _disposed;

    public AutomationSessionRegistry(ILogger<AutomationSessionRegistry> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        this._logger = logger;
    }

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

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref this._disposed, 1) != 0)
        {
            return;
        }

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
                this._logger.LogError(ex, "Failed to dispose automation session {SessionId}", registration.SessionId);
            }
        }
    }
}
