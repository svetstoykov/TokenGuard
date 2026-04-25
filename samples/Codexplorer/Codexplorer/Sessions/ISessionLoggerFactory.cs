using WorkspaceModel = Codexplorer.Workspace.Workspace;

namespace Codexplorer.Sessions;

/// <summary>
/// Creates one per-session session logger scoped to one workspace and live chat session.
/// </summary>
/// <remarks>
/// The factory is application-scoped and resolves stable configuration once. Individual loggers remain per-session so
/// each repo chat session receives its own transcript file and event stream.
/// </remarks>
public interface ISessionLoggerFactory
{
    /// <summary>
    /// Begins a new session transcript for one interactive repo session.
    /// </summary>
    /// <param name="workspace">The workspace the query operates against.</param>
    /// <param name="sessionLabel">The short human-readable session label that should appear in the transcript header.</param>
    /// <returns>A new session logger with its transcript file already created and initialized.</returns>
    ISessionLogger BeginSession(WorkspaceModel workspace, string sessionLabel);
}
