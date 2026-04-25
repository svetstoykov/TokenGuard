namespace Codexplorer.Workspace;

/// <summary>
/// Abstracts git clone execution for workspace creation.
/// </summary>
/// <remarks>
/// This seam keeps <see cref="IWorkspaceManager"/> free from direct LibGit2Sharp calls so tests and
/// alternate clone implementations can supply deterministic behavior without network access.
/// </remarks>
public interface IGitCloner
{
    /// <summary>
    /// Clones a repository into the specified destination folder.
    /// </summary>
    /// <param name="url">The git remote URL to clone.</param>
    /// <param name="destinationPath">The local destination folder.</param>
    /// <param name="depth">The shallow clone depth to request. Use 0 for the provider default.</param>
    /// <param name="ct">The cancellation token for the operation.</param>
    /// <returns>A task that completes when the clone finishes.</returns>
    Task CloneAsync(string url, string destinationPath, int depth, CancellationToken ct = default);
}
