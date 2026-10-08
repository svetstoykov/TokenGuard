using WorkspaceModel = Codexplorer.Workspace.Workspace;

namespace Codexplorer.Agent;

/// <summary>Defines the sample's automation session creation capability.</summary>
internal interface IAutomationExplorerAgent
{
    /// <summary>Creates a session with a hard total provider-call allowance.</summary>
    /// <param name="workspace">The workspace to explore.</param>
    /// <param name="modelCallBudget">The optional total provider-call allowance.</param>
    /// <param name="wrapUpWindow">The optional model-call window reserved for the runner wrap-up prompt.</param>
    /// <returns>The newly created exploration session.</returns>
    IExplorerSession StartAutomationSession(WorkspaceModel workspace, int? modelCallBudget, int? wrapUpWindow = null);
}
