using System.Text;
using System.Text.Json;
using WorkspaceModel = Codexplorer.Workspace.Workspace;

namespace Codexplorer.Tools;

/// <summary>
/// Lists direct children of one workspace directory.
/// </summary>
/// <remarks>
/// Use this tool to inspect folder contents before deciding whether to read a file, search with
/// <c>grep</c>, or drill deeper with <c>file_tree</c>. Output is capped to prevent large directories
/// from flooding model context.
/// </remarks>
public sealed class ListDirectoryTool : IWorkspaceTool
{
    /// <summary>
    /// Maximum number of directory entries returned by one call.
    /// </summary>
    public const int EntryCap = 500;

    private static readonly ToolSchema CachedSchema = ToolSchema.CreateFunction(
        "list_directory",
        "List direct children of one workspace-relative directory. Use this to inspect one folder quickly before reading files or expanding a deeper tree.",
        """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "path": {
              "type": "string",
              "description": "Workspace-relative directory path to list, such as \".\" or \"src/Tools\"."
            }
          },
          "required": ["path"]
        }
        """);

    /// <summary>
    /// Gets tool name exposed to the model.
    /// </summary>
    public string Name => "list_directory";

    /// <summary>
    /// Gets cached OpenAI-compatible schema for this tool.
    /// </summary>
    public ToolSchema Schema => CachedSchema;

    /// <summary>
    /// Represents arguments for <see cref="ListDirectoryTool"/>.
    /// </summary>
    /// <param name="Path">The workspace-relative directory path to list.</param>
    public sealed record Parameters(string Path);

    Task<string> IWorkspaceTool.ExecuteAsync(JsonElement arguments, WorkspaceModel workspace, CancellationToken ct)
    {
        return this.HandleAsync(ToolRegistry.DeserializeArguments<Parameters>(arguments), workspace, ct);
    }

    /// <summary>
    /// Lists one directory and formats each child as name, type, and size.
    /// </summary>
    /// <param name="parameters">Typed tool arguments.</param>
    /// <param name="workspace">The workspace that constrains file access.</param>
    /// <param name="ct">The cancellation token for the current tool call.</param>
    /// <returns>Tab-separated directory entries, or a recoverable error string.</returns>
    public Task<string> HandleAsync(Parameters parameters, WorkspaceModel workspace, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(workspace);
        ct.ThrowIfCancellationRequested();

        var requestedPath = string.IsNullOrWhiteSpace(parameters.Path) ? "." : parameters.Path;
        var resolvedPath = PathGuard.ResolvePath(workspace.LocalPath, requestedPath);

        if (!Directory.Exists(resolvedPath))
        {
            return Task.FromResult($"Error: directory not found: {ToolResultFormatting.NormalizePath(requestedPath)}");
        }

        var entries = new DirectoryInfo(resolvedPath)
            .EnumerateFileSystemInfos()
            .OrderBy(static entry => entry.Attributes.HasFlag(FileAttributes.Directory) ? 0 : 1)
            .ThenBy(static entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var displayedEntries = entries.Take(EntryCap).ToArray();
        var builder = new StringBuilder();
        builder.Append("name\ttype\tsizeBytes");

        foreach (var entry in displayedEntries)
        {
            builder.AppendLine();
            builder.Append(entry.Name);
            builder.Append('\t');
            builder.Append(entry.Attributes.HasFlag(FileAttributes.Directory) ? "directory" : "file");
            builder.Append('\t');
            builder.Append(entry is FileInfo fileInfo ? fileInfo.Length : 0L);
        }

        if (entries.Length > EntryCap)
        {
            builder.AppendLine();
            builder.Append(ToolResultFormatting.TruncationMarker(entries.Length - EntryCap, EntryCap, "entries"));
        }

        return Task.FromResult(builder.ToString());
    }
}
