using WorkspaceModel = Codexplorer.Workspace.Workspace;

namespace Codexplorer.Sessions;

/// <summary>
///     Defines creation of the directory a new session writes to.
/// </summary>
/// <remarks>
///     The implementation is application-scoped and does not need to be thread-safe beyond one session start at a time.
/// </remarks>
public interface ISessionDirectoryFactory
{
    /// <summary>
    ///     Creates the session directory and its empty <c>artifacts</c> folder.
    /// </summary>
    /// <param name="workspace">The workspace the session explores. Cannot be <see langword="null" />.</param>
    /// <param name="requestedPath">
    ///     The absolute directory chosen by the caller, or <see langword="null" /> to create a new directory named after
    ///     the start time and repository under the configured sessions root.
    /// </param>
    /// <returns>The created session directory.</returns>
    SessionDirectory Create(WorkspaceModel workspace, string? requestedPath);
}
