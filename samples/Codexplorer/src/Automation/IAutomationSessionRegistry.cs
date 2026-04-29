using Codexplorer.Agent;
using WorkspaceModel = Codexplorer.Workspace.Workspace;

namespace Codexplorer.Automation;

internal interface IAutomationSessionRegistry : IAsyncDisposable
{
    AutomationSessionRegistration Add(WorkspaceModel workspace, IExplorerSession session);

    bool TryGet(string sessionId, out AutomationSessionRegistration? session);

    bool TryRemove(string sessionId, out AutomationSessionRegistration? session);
}
