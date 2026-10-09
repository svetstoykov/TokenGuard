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
    /// <param name="commitSha">
    /// The full commit SHA to check out with its complete history, ignoring <paramref name="depth"/>; or
    /// <see langword="null"/> to clone the default branch.
    /// </param>
    /// <param name="ct">The cancellation token for the operation.</param>
    /// <returns>A task that completes when the clone finishes.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="commitSha"/> cannot be fetched from <paramref name="url"/>.</exception>
    Task CloneAsync(string url, string destinationPath, int depth, string? commitSha = null, CancellationToken ct = default);
}
