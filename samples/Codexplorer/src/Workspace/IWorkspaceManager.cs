namespace Codexplorer.Workspace;

/// <summary>
/// Provides repository workspace discovery and cloning for GitHub-backed workspaces.
/// </summary>
/// <remarks>
/// Implementations normalize supported GitHub URL forms into one stable workspace identity so the
/// rest of Codexplorer can operate against predictable local repository folders.
/// </remarks>
public interface IWorkspaceManager
{
    /// <summary>
    /// Clones a supported GitHub repository into the configured workspace root.
    /// </summary>
    /// <param name="githubUrl">The GitHub repository URL in a supported HTTPS or SSH form.</param>
    /// <param name="forceReclone">
    /// <see langword="true"/> to delete any existing destination folder and clone again; otherwise an
    /// existing tracked workspace is returned unchanged.
    /// </param>
    /// <param name="ct">The cancellation token for the clone operation.</param>
    /// <returns>The tracked <see cref="Workspace"/> entry for the cloned repository.</returns>
    Task<Workspace> CloneAsync(string githubUrl, bool forceReclone = false, CancellationToken ct = default);

    /// <summary>
    /// Lists tracked workspaces already present under the configured workspace root.
    /// </summary>
    /// <returns>Tracked workspaces sorted by <see cref="Workspace.ClonedAt"/> descending.</returns>
    IReadOnlyList<Workspace> ListExisting();

    /// <summary>
    /// Finds a tracked workspace by <c>{owner}/{repo}</c>.
    /// </summary>
    /// <param name="ownerRepo">The repository identity in <c>{owner}/{repo}</c> form.</param>
    /// <returns>The matching <see cref="Workspace"/>, or <see langword="null"/> when none exists.</returns>
    Workspace? Find(string ownerRepo);
}
