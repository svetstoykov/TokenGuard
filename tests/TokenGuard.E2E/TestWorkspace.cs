namespace TokenGuard.E2E;

/// <summary>
/// Represents a workspace directory used by a single E2E run.
/// </summary>
public sealed record TestWorkspace(string DirectoryPath, bool DeleteOnDispose = true) : IDisposable
{
    /// <summary>
    /// Creates an empty temporary workspace under the system temp directory.
    /// </summary>
    /// <param name="directoryPrefix">Stable folder prefix that makes leaked test directories easier to identify.</param>
    public static TestWorkspace Create(string directoryPrefix)
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), directoryPrefix, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directoryPath);
        return new TestWorkspace(directoryPath);
    }

    /// <summary>
    /// Creates an empty workspace under <see cref="AppContext.BaseDirectory"/>.
    /// </summary>
    /// <param name="directoryPrefix">Stable folder prefix that groups related benchmark workspaces.</param>
    /// <param name="rootDirectoryName">Root folder name created under <see cref="AppContext.BaseDirectory"/>.</param>
    /// <param name="deleteOnDispose">Whether to delete workspace when disposed.</param>
    public static TestWorkspace CreateInBaseDirectory(
        string directoryPrefix,
        string rootDirectoryName = "benchmarks",
        bool deleteOnDispose = false)
    {
        var rootDirectoryPath = Path.Combine(AppContext.BaseDirectory, rootDirectoryName);
        var directoryPath = Path.Combine(rootDirectoryPath, directoryPrefix, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directoryPath);
        return new TestWorkspace(directoryPath, deleteOnDispose);
    }

    /// <summary>
    /// Deletes the temporary workspace and all generated files.
    /// </summary>
    public void Dispose()
    {
        if (this.DeleteOnDispose && Directory.Exists(this.DirectoryPath))
        {
            Directory.Delete(this.DirectoryPath, recursive: true);
        }
    }
}
