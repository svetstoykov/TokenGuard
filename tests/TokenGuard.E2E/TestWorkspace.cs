namespace TokenGuard.E2E;

internal sealed record TestWorkspace(string DirectoryPath) : IDisposable
{
    public static TestWorkspace Create(string directoryPrefix)
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), directoryPrefix, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directoryPath);
        return new TestWorkspace(directoryPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(this.DirectoryPath))
        {
            Directory.Delete(this.DirectoryPath, recursive: true);
        }
    }
}
