namespace TokenGuard.E2E;

/// <summary>
/// Represents a temporary workspace directory used by a single E2E run.
/// </summary>
public sealed record TestWorkspace(string DirectoryPath) : IDisposable
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
    /// Deletes the temporary workspace and all generated files.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(this.DirectoryPath))
        {
            Directory.Delete(this.DirectoryPath, recursive: true);
        }
    }
}
