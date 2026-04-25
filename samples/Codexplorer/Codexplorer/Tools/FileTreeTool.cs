using System.Text;
using System.Text.Json;
using WorkspaceModel = Codexplorer.Workspace.Workspace;

namespace Codexplorer.Tools;

/// <summary>
/// Renders an indented directory tree under one workspace path.
/// </summary>
/// <remarks>
/// Use this when directory shape matters more than file contents, for example to understand module
/// layout or spot sibling folders before reading specific files.
/// </remarks>
public sealed class FileTreeTool : IWorkspaceTool
{
    /// <summary>
    /// Maximum number of tree nodes returned by one call.
    /// </summary>
    public const int NodeCap = 1000;

    private static readonly ToolSchema CachedSchema = ToolSchema.CreateFunction(
        "file_tree",
        "Render an indented workspace file tree. Use this to understand repository structure faster than listing many directories one by one.",
        """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "path": {
              "type": "string",
              "description": "Optional workspace-relative directory to use as tree root."
            },
            "maxDepth": {
              "type": "integer",
              "description": "Optional maximum directory depth, where 0 returns only the root node."
            }
          }
        }
        """);

    /// <summary>
    /// Gets tool name exposed to the model.
    /// </summary>
    public string Name => "file_tree";

    /// <summary>
    /// Gets cached OpenAI-compatible schema for this tool.
    /// </summary>
    public ToolSchema Schema => CachedSchema;

    /// <summary>
    /// Represents arguments for <see cref="FileTreeTool"/>.
    /// </summary>
    /// <param name="Path">The optional workspace-relative directory root.</param>
    /// <param name="MaxDepth">The optional maximum tree depth.</param>
    public sealed record Parameters(string? Path, int? MaxDepth);

    Task<string> IWorkspaceTool.ExecuteAsync(JsonElement arguments, WorkspaceModel workspace, CancellationToken ct)
    {
        return this.HandleAsync(ToolRegistry.DeserializeArguments<Parameters>(arguments), workspace, ct);
    }

    /// <summary>
    /// Renders one directory tree with indentation and a hard node cap.
    /// </summary>
    /// <param name="parameters">Typed tool arguments.</param>
    /// <param name="workspace">The workspace that constrains file access.</param>
    /// <param name="ct">The cancellation token for the current tool call.</param>
    /// <returns>An indented tree, a truncation marker, or a recoverable error string.</returns>
    public Task<string> HandleAsync(Parameters parameters, WorkspaceModel workspace, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(workspace);
        ct.ThrowIfCancellationRequested();

        if (parameters.MaxDepth is < 0)
        {
            return Task.FromResult("Error: maxDepth must be greater than or equal to 0");
        }

        var requestedPath = string.IsNullOrWhiteSpace(parameters.Path) ? "." : parameters.Path;
        var resolvedPath = PathGuard.ResolvePath(workspace.LocalPath, requestedPath);

        if (!Directory.Exists(resolvedPath))
        {
            return Task.FromResult($"Error: directory not found: {ToolResultFormatting.NormalizePath(requestedPath)}");
        }

        var lines = new List<string>(Math.Min(NodeCap, 128));
        var displayRoot = ToolResultFormatting.NormalizePath(requestedPath);
        var totalNodes = 0;
        Traverse(new DirectoryInfo(resolvedPath), displayRoot, depth: 0, parameters.MaxDepth, lines, ref totalNodes);

        var builder = new StringBuilder();
        builder.AppendJoin(Environment.NewLine, lines);

        if (totalNodes > NodeCap)
        {
            builder.AppendLine();
            builder.Append(ToolResultFormatting.TruncationMarker(totalNodes - NodeCap, NodeCap, "nodes"));
        }

        return Task.FromResult(builder.ToString());
    }

    private static void Traverse(
        DirectoryInfo directory,
        string displayPath,
        int depth,
        int? maxDepth,
        List<string> lines,
        ref int totalNodes)
    {
        totalNodes++;

        if (lines.Count < NodeCap)
        {
            lines.Add(depth == 0 ? displayPath : $"{new string(' ', depth * 2)}{directory.Name}/");
        }

        if (maxDepth is not null && depth >= maxDepth.Value)
        {
            return;
        }

        var entries = directory.EnumerateFileSystemInfos()
            .OrderBy(static entry => entry.Attributes.HasFlag(FileAttributes.Directory) ? 0 : 1)
            .ThenBy(static entry => entry.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            if (entry.Attributes.HasFlag(FileAttributes.Directory))
            {
                Traverse((DirectoryInfo)entry, displayPath, depth + 1, maxDepth, lines, ref totalNodes);
                continue;
            }

            totalNodes++;

            if (lines.Count < NodeCap)
            {
                lines.Add($"{new string(' ', (depth + 1) * 2)}{entry.Name}");
            }
        }
    }
}
