using WorkspaceModel = Codexplorer.Workspace.Workspace;

namespace Codexplorer.Sessions;

/// <summary>
/// Creates one per-query session logger scoped to one workspace and user request.
/// </summary>
/// <remarks>
/// The factory is application-scoped and resolves stable configuration once. Individual loggers remain per-session so
/// each query receives its own transcript file and event stream.
/// </remarks>
public interface ISessionLoggerFactory
{
    /// <summary>
    /// Begins a new session transcript for one user query.
    /// </summary>
    /// <param name="workspace">The workspace the query operates against.</param>
    /// <param name="userQuery">The original user query that should appear in the transcript header.</param>
    /// <returns>A new session logger with its transcript file already created and initialized.</returns>
    ISessionLogger BeginSession(WorkspaceModel workspace, string userQuery);
}
