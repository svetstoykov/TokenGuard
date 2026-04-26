using WorkspaceModel = Codexplorer.Workspace.Workspace;

namespace Codexplorer.Tools;

internal static class ToolResultFormatting
{
    public static string TruncationMarker(int omittedCount, int cap, string unit)
    {
        return $"[... truncated: {omittedCount} more {unit}; cap {cap} {unit} hit ...]";
    }

    public static string ToWorkspaceRelativePath(WorkspaceModel workspace, string absolutePath)
    {
        var workspaceRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspace.LocalPath));
        var relativePath = Path.GetRelativePath(workspaceRoot, absolutePath);
        return NormalizePath(relativePath);
    }

    public static string NormalizePath(string path)
    {
        var normalizedPath = path
            .Replace('\\', '/')
            .Replace(Path.DirectorySeparatorChar, '/');

        return normalizedPath.StartsWith("./", StringComparison.Ordinal)
            ? normalizedPath[2..]
            : normalizedPath;
    }
}
