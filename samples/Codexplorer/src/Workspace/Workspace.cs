namespace Codexplorer.Workspace;

/// <summary>
/// Represents one locally tracked repository workspace.
/// </summary>
/// <param name="Name">The repository name component, such as <c>Hello-World</c>.</param>
/// <param name="OwnerRepo">The GitHub repository identity in <c>{owner}/{repo}</c> form.</param>
/// <param name="LocalPath">The local filesystem path to the workspace root.</param>
/// <param name="ClonedAt">The UTC timestamp when Codexplorer first tracked the workspace.</param>
/// <param name="SizeBytes">The current on-disk size of the workspace in bytes.</param>
/// <remarks>
/// This record is the stable contract exchanged between workspace discovery, clone operations, and
/// later tool execution that needs a concrete repository checkout.
/// </remarks>
public sealed record Workspace(
    string Name,
    string OwnerRepo,
    string LocalPath,
    DateTime ClonedAt,
    long SizeBytes);
