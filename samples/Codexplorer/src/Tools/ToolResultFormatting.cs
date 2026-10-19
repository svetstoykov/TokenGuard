using WorkspaceModel = Codexplorer.Workspace.Workspace;

namespace Codexplorer.Tools;

internal static class ToolResultFormatting
{
    public static string TruncationMarker(int omittedCount, int cap, string unit)
    {
        return $"[... truncated: {omittedCount} more {unit}; cap {cap} {unit} hit ...]";
    }

    public static string TruncationMarker(int omittedCount, string unit, int cap, string capUnit, string advice)
    {
        return $"[... truncated: {omittedCount} more {unit}; cap {cap} {capUnit} hit; {advice} ...]";
    }

    public static string CutLine(string line, int cap)
    {
        if (line.Length <= cap)
        {
            return line;
        }

        var keptLength = char.IsHighSurrogate(line[cap - 1]) ? cap - 1 : cap;
        return $"{line[..keptLength]} [... line cut at {cap} characters ...]";
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
