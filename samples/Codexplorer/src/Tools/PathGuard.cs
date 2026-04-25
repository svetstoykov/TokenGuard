namespace Codexplorer.Tools;

/// <summary>
/// Resolves workspace-relative paths while preventing traversal and symlink escape.
/// </summary>
/// <remarks>
/// This helper is security-sensitive because every filesystem tool relies on it to constrain access
/// to one cloned repository. It rejects absolute paths, rejects any <c>..</c> segment up front, and
/// resolves symlinks segment by segment so a link cannot silently jump outside the workspace root.
/// </remarks>
public static class PathGuard
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>
    /// Resolves one user-supplied relative path against one workspace root.
    /// </summary>
    /// <param name="workspaceRoot">The absolute or relative workspace root directory.</param>
    /// <param name="relativePath">The user-supplied path relative to <paramref name="workspaceRoot"/>.</param>
    /// <returns>The normalized absolute path inside the workspace root.</returns>
    /// <exception cref="PathEscapeException">
    /// Thrown when <paramref name="relativePath"/> is absolute, contains <c>..</c>, or escapes through a symlink.
    /// </exception>
    public static string ResolvePath(string workspaceRoot, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        var normalizedRoot = NormalizeAbsolutePath(workspaceRoot);
        var rootWithSeparator = EnsureTrailingSeparator(normalizedRoot);
        var candidateRelativePath = string.IsNullOrWhiteSpace(relativePath) ? "." : NormalizeSeparators(relativePath);

        if (Path.IsPathRooted(candidateRelativePath))
        {
            throw new PathEscapeException(candidateRelativePath, normalizedRoot, "Absolute paths are not allowed.");
        }

        var segments = candidateRelativePath.Split(
            Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Any(static segment => segment == ".."))
        {
            throw new PathEscapeException(candidateRelativePath, normalizedRoot, "Parent directory traversal is not allowed.");
        }

        var currentPath = normalizedRoot;

        foreach (var segment in segments)
        {
            if (segment == ".")
            {
                continue;
            }

            currentPath = NormalizeAbsolutePath(Path.Combine(currentPath, segment));
            EnsureInsideWorkspace(currentPath, normalizedRoot, rootWithSeparator, candidateRelativePath);

            if (!TryGetExistingEntry(currentPath, out var entry))
            {
                continue;
            }

            if (entry.LinkTarget is null)
            {
                continue;
            }

            var resolvedTarget = entry.ResolveLinkTarget(returnFinalTarget: true);

            if (resolvedTarget is null)
            {
                continue;
            }

            currentPath = NormalizeAbsolutePath(resolvedTarget.FullName);
            EnsureInsideWorkspace(currentPath, normalizedRoot, rootWithSeparator, candidateRelativePath);
        }

        return currentPath;
    }

    private static bool TryGetExistingEntry(string path, out FileSystemInfo entry)
    {
        if (Directory.Exists(path))
        {
            entry = new DirectoryInfo(path);
            return true;
        }

        if (File.Exists(path))
        {
            entry = new FileInfo(path);
            return true;
        }

        entry = null!;
        return false;
    }

    private static void EnsureInsideWorkspace(
        string candidatePath,
        string normalizedRoot,
        string rootWithSeparator,
        string userPath)
    {
        if (string.Equals(candidatePath, normalizedRoot, PathComparison))
        {
            return;
        }

        if (!candidatePath.StartsWith(rootWithSeparator, PathComparison))
        {
            throw new PathEscapeException(userPath, normalizedRoot, "Resolved path is outside the workspace root.");
        }
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static string NormalizeAbsolutePath(string path)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static string NormalizeSeparators(string path)
    {
        return path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
    }
}
