using WorkspaceModel = Codexplorer.Workspace.Workspace;

namespace Codexplorer.Agent;

/// <summary>
/// Starts one repository exploration session against a workspace-scoped agent loop.
/// </summary>
/// <remarks>
/// An explorer agent creates long-lived chat sessions that keep one TokenGuard conversation context alive across
/// multiple user turns, model calls, tool invocations, and transcript writes.
/// </remarks>
public interface IExplorerAgent
{
    /// <summary>
    /// Starts one interactive exploration session for one cloned workspace.
    /// </summary>
    /// <param name="workspace">The workspace to inspect.</param>
    /// <returns>A new long-lived explorer session.</returns>
    IExplorerSession StartSession(WorkspaceModel workspace);
}

/// <summary>
/// Represents one long-lived repository exploration chat session.
/// </summary>
/// <remarks>
/// A session owns one TokenGuard conversation context and one markdown transcript for its full lifetime, allowing the
/// user to continue a repo conversation across many messages without resetting context between turns.
/// </remarks>
public interface IExplorerSession : IAsyncDisposable
{
    /// <summary>
    /// Gets the absolute markdown transcript path for the active session.
    /// </summary>
    string LogFilePath { get; }

    /// <summary>
    /// Submits one user message into the current session.
    /// </summary>
    /// <param name="userMessage">The new user message to add to the live conversation.</param>
    /// <param name="ct">The cancellation token for this message exchange.</param>
    /// <returns>The outcome of handling this one user message inside the ongoing session.</returns>
    Task<AgentExchangeResult> SubmitAsync(string userMessage, CancellationToken ct);
}
