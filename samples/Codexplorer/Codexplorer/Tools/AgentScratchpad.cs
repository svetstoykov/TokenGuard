using System.Text;
using WorkspaceModel = Codexplorer.Workspace.Workspace;

namespace Codexplorer.Tools;

/// <summary>
/// Resolves and creates the agent-owned scratch area inside one workspace.
/// </summary>
/// <remarks>
/// Codexplorer's mutable tools are intentionally constrained to a hidden scratch directory so the
/// model can keep notes and intermediate artifacts without editing repository source files.
/// </remarks>
internal static class AgentScratchpad
{
    internal const string RootDirectoryName = ".codexplorer";

    internal static readonly Encoding Utf8WithoutBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    internal static string ResolvePath(WorkspaceModel workspace, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        return PathGuard.ResolvePath(Path.Combine(workspace.LocalPath, RootDirectoryName), relativePath);
    }

    internal static string GetWorkspaceRelativePath(WorkspaceModel workspace, string absolutePath)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);

        return ToolResultFormatting.ToWorkspaceRelativePath(workspace, absolutePath);
    }

    internal static void EnsureParentDirectoryExists(string absolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);

        var parentDirectory = Path.GetDirectoryName(absolutePath)
            ?? throw new InvalidOperationException($"Path '{absolutePath}' does not have a parent directory.");

        Directory.CreateDirectory(parentDirectory);
    }
}
