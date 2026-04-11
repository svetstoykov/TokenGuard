namespace TokenGuard.Tools.Tools;

/// <summary>
/// Resolves model-supplied relative paths inside a bounded workspace directory.
/// </summary>
/// <remarks>
/// Centralizing path validation ensures every shared filesystem tool applies the same traversal rules and keeps all
/// E2E side effects inside the disposable test workspace.
/// </remarks>
public static class WorkspacePathResolver
{
    /// <summary>
    /// Resolves a relative path inside the workspace.
    /// </summary>
    /// <param name="workspaceDirectory">The absolute workspace root.</param>
    /// <param name="relativePath">The model-supplied path to resolve.</param>
    /// <returns>The absolute path inside <paramref name="workspaceDirectory"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when the workspace or relative path is invalid.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the resolved path escapes the workspace root.</exception>
    public static string Resolve(string workspaceDirectory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(workspaceDirectory))
        {
            throw new ArgumentException("Workspace directory is required.", nameof(workspaceDirectory));
        }

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Relative path is required.", nameof(relativePath));
        }

        var workspaceRoot = Path.GetFullPath(workspaceDirectory);
        var candidate = Path.GetFullPath(Path.Combine(workspaceRoot, relativePath));
        var rootWithSeparator = workspaceRoot.EndsWith(Path.DirectorySeparatorChar)
            ? workspaceRoot
            : workspaceRoot + Path.DirectorySeparatorChar;

        if (!candidate.StartsWith(rootWithSeparator, StringComparison.Ordinal) &&
            !string.Equals(candidate, workspaceRoot, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Path must stay within the task workspace.");
        }

        return candidate;
    }
}
