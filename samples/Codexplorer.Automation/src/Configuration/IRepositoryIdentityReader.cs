namespace Codexplorer.Automation.Configuration;

/// <summary>Defines capture of the TokenGuard checkout identity.</summary>
internal interface IRepositoryIdentityReader
{
    /// <summary>Asynchronously captures the commit and worktree state.</summary>
    /// <param name="repositoryPath">The optional explicit checkout path.</param>
    /// <param name="ct">The token observed while reading repository identity.</param>
    /// <returns>A task containing the repository identity.</returns>
    Task<RepositoryIdentity> ReadAsync(string? repositoryPath, CancellationToken ct);
}
