namespace Codexplorer.Configuration;

internal static class CodexplorerPathResolver
{
    public static string ResolveFromAppBaseDirectory(string? path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(path, AppContext.BaseDirectory);
    }
}
