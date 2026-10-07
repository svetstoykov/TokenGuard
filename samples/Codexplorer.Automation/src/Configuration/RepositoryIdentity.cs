namespace Codexplorer.Automation.Configuration;

/// <summary>Represents the repository identity captured before a run starts.</summary>
/// <param name="CommitSha">The checkout commit SHA.</param>
/// <param name="Dirty">Whether the checkout has uncommitted changes.</param>
internal sealed record RepositoryIdentity(string CommitSha, bool Dirty);
