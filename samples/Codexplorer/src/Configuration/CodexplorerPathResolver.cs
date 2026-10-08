namespace Codexplorer.Configuration;

internal static class CodexplorerPathResolver
{
    public static string ResolveFromAppBaseDirectory(string? path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(path, AppContext.BaseDirectory);
    }

    /// <summary>
    ///     Resolves an output path against the repository that contains the application.
    /// </summary>
    /// <param name="path">The configured path. Cannot be <see langword="null" /> or empty.</param>
    /// <returns>
    ///     <paramref name="path" /> when it is absolute; otherwise the path under the nearest ancestor of the application
    ///     folder that contains <c>.git</c>, or under the application folder when there is no such ancestor.
    /// </returns>
    public static string ResolveFromRepositoryRoot(string? path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(path, FindRepositoryRoot(AppContext.BaseDirectory) ?? AppContext.BaseDirectory);
    }

    private static string? FindRepositoryRoot(string start)
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            var gitPath = Path.Combine(directory.FullName, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
            {
                return directory.FullName;
            }
        }

        return null;
    }
}
