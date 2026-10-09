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
    private const string RemoteName = "origin";

    /// <inheritdoc />
    /// <remarks>
    /// A pinned commit is fetched by SHA into an empty repository and checked out with a detached head, because
    /// LibGit2Sharp applies a shallow depth only when it clones a branch.
    /// </remarks>
    public Task CloneAsync(string url, string destinationPath, int depth, string? commitSha = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ct.ThrowIfCancellationRequested();

        return Task.Run(
            () =>
            {
                if (commitSha is not null)
                {
                    CloneAtCommit(url, destinationPath, commitSha);
                    return;
                }

                var cloneOptions = new CloneOptions();

                if (depth > 0)
                {
                    cloneOptions.FetchOptions.Depth = depth;
                }

                Repository.Clone(url, destinationPath, cloneOptions);
            },
            ct);
    }

    private static void CloneAtCommit(string url, string destinationPath, string commitSha)
    {
        try
        {
            Repository.Init(destinationPath);

            using var repository = new Repository(destinationPath);
            repository.Network.Remotes.Add(RemoteName, url);
            Commands.Fetch(repository, RemoteName, [commitSha], new FetchOptions { TagFetchMode = TagFetchMode.None }, logMessage: null);

            var commit = repository.Lookup<Commit>(commitSha)
                ?? throw new NotFoundException($"Object '{commitSha}' is not a commit.");
            Commands.Checkout(repository, commit);
        }
        catch (LibGit2SharpException ex)
        {
            throw new InvalidOperationException($"Commit '{commitSha}' could not be fetched from repository '{url}': {ex.Message}", ex);
        }
    }
}
