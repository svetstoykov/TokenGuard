namespace Codexplorer.Automation.Configuration;

internal static class AutomationPathResolver
{
    public static string ResolveFromCurrentDirectory(string? path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(path, Directory.GetCurrentDirectory());
    }
}
