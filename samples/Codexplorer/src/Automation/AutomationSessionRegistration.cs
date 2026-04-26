using Codexplorer.Agent;
using WorkspaceModel = Codexplorer.Workspace.Workspace;

namespace Codexplorer.Automation;

internal sealed record AutomationSessionRegistration(
    string SessionId,
    WorkspaceModel Workspace,
    IExplorerSession Session)
{
    public string LogFilePath => this.Session.LogFilePath;
}
