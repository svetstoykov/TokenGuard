namespace Codexplorer.Automation.Protocol;

internal sealed record OpenSessionResponse(
    string SessionId,
    AutomationWorkspace Workspace,
    string LogFilePath);
