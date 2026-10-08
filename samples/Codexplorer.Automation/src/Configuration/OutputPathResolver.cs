namespace Codexplorer.Automation.Configuration;

/// <summary>
///     Provides resolution of the report output directory.
/// </summary>
internal static class OutputPathResolver
{
    /// <summary>
    ///     Resolves an output path against the repository that contains the application.
    /// </summary>
    /// <param name="path">The configured path. Cannot be <see langword="null" /> or empty.</param>
    /// <param name="applicationDirectory">The folder the application runs from. Cannot be <see langword="null" /> or empty.</param>
    /// <returns>
    ///     <paramref name="path" /> when it is absolute; otherwise the path under the nearest ancestor of
    ///     <paramref name="applicationDirectory" /> that contains <c>.git</c>, or under <paramref name="applicationDirectory" />
    ///     when there is no such ancestor.
    /// </returns>
    public static string Resolve(string path, string applicationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDirectory);
        return Path.GetFullPath(path, FindRepositoryRoot(applicationDirectory) ?? applicationDirectory);
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
