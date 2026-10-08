using Codexplorer.Configuration;
using Microsoft.Extensions.Options;
using WorkspaceModel = Codexplorer.Workspace.Workspace;

namespace Codexplorer.Sessions;

/// <summary>
///     Creates session directories under the configured sessions root or at a caller-chosen path.
/// </summary>
/// <remarks>
///     A relative sessions root resolves against the repository that contains the application, so interactive sessions
///     land in one place regardless of the build output folder.
/// </remarks>
public sealed class SessionDirectoryFactory : ISessionDirectoryFactory
{
    private readonly CodexplorerOptions _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SessionDirectoryFactory" /> class.
    /// </summary>
    /// <param name="options">The validated Codexplorer options snapshot. Cannot be <see langword="null" />.</param>
    public SessionDirectoryFactory(IOptions<CodexplorerOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        this._options = options.Value;
    }

    /// <inheritdoc />
    public SessionDirectory Create(WorkspaceModel workspace, string? requestedPath)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var path = string.IsNullOrWhiteSpace(requestedPath) ? this.CreateUniquePath(workspace) : Path.GetFullPath(requestedPath);
        if (IsInside(workspace.LocalPath, path))
        {
            throw new InvalidOperationException($"Session directory '{path}' is inside the cloned repository '{workspace.LocalPath}'.");
        }

        var directory = new SessionDirectory(path);
        Directory.CreateDirectory(directory.ArtifactsPath);
        return directory;
    }

    private string CreateUniquePath(WorkspaceModel workspace)
    {
        var sessionLogsDirectory = this._options.Logging?.SessionLogsDirectory
            ?? throw new InvalidOperationException("Codexplorer session logs directory is not configured.");
        var root = CodexplorerPathResolver.ResolveFromRepositoryRoot(sessionLogsDirectory);
        var sessionId = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{SessionSlug.Create(workspace.OwnerRepo)}";

        for (var attempt = 0; ; attempt++)
        {
            var candidatePath = Path.Combine(root, attempt == 0 ? sessionId : $"{sessionId}-{attempt}");
            if (!Directory.Exists(candidatePath))
            {
                return candidatePath;
            }
        }
    }

    private static bool IsInside(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }
}
