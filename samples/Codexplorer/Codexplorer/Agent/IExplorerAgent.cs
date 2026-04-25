using WorkspaceModel = Codexplorer.Workspace.Workspace;

namespace Codexplorer.Agent;

/// <summary>
/// Runs one repository exploration query against a workspace-scoped agent loop.
/// </summary>
/// <remarks>
/// An explorer agent owns the full TokenGuard-managed request loop for one user query, including context preparation,
/// model calls, tool execution, and terminal result synthesis.
/// </remarks>
public interface IExplorerAgent
{
    /// <summary>
    /// Executes one exploration query against one cloned workspace.
    /// </summary>
    /// <param name="workspace">The workspace to inspect.</param>
    /// <param name="userQuery">The natural-language query to answer.</param>
    /// <param name="ct">The external cancellation token for the run.</param>
    /// <returns>The terminal result describing how the run completed.</returns>
    Task<AgentRunResult> RunAsync(WorkspaceModel workspace, string userQuery, CancellationToken ct);
}
