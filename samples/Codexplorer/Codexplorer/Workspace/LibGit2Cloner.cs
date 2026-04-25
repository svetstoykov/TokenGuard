using LibGit2Sharp;

namespace Codexplorer.Workspace;

/// <summary>
/// Clones repositories through LibGit2Sharp.
/// </summary>
/// <remarks>
/// This adapter keeps LibGit2Sharp-specific options isolated behind <see cref="IGitCloner"/> so the
/// workspace manager only depends on a minimal clone contract.
/// </remarks>
public sealed class LibGit2Cloner : IGitCloner
{
    /// <inheritdoc />
    public Task CloneAsync(string url, string destinationPath, int depth, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ct.ThrowIfCancellationRequested();

        return Task.Run(
            () =>
            {
                var cloneOptions = new CloneOptions();

                if (depth > 0)
                {
                    cloneOptions.FetchOptions.Depth = depth;
                }

                Repository.Clone(url, destinationPath, cloneOptions);
            },
            ct);
    }
}
