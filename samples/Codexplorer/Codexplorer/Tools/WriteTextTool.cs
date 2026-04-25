using System.Text.Json;
using WorkspaceModel = Codexplorer.Workspace.Workspace;

namespace Codexplorer.Tools;

/// <summary>
/// Writes text into an existing file under the agent-owned scratch directory.
/// </summary>
/// <remarks>
/// This tool only operates on files that already exist under <c>.codexplorer</c>, which keeps the
/// create-versus-update workflow explicit and prevents accidental edits to repository source files.
/// </remarks>
public sealed class WriteTextTool : IWorkspaceTool
{
    private static readonly ToolSchema CachedSchema = ToolSchema.CreateFunction(
        "write_text",
        "Write UTF-8 text into an existing file under the agent-owned scratch directory `.codexplorer`. Use mode `replace` to overwrite the file or `append` to add more text. Fails if the file does not exist.",
        """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "path": {
              "type": "string",
              "description": "Path relative to the `.codexplorer` scratch directory, such as `notes/summary.txt`."
            },
            "content": {
              "type": "string",
              "description": "Text to write. When mode is `replace`, this becomes the full file contents. When mode is `append`, this text is added to the end."
            },
            "mode": {
              "type": "string",
              "enum": ["replace", "append"],
              "description": "Whether to overwrite the file or append to it."
            }
          },
          "required": ["path", "content", "mode"]
        }
        """);

    /// <summary>
    /// Gets tool name exposed to the model.
    /// </summary>
    public string Name => "write_text";

    /// <summary>
    /// Gets cached OpenAI-compatible schema for this tool.
    /// </summary>
    public ToolSchema Schema => CachedSchema;

    /// <summary>
    /// Represents arguments for <see cref="WriteTextTool"/>.
    /// </summary>
    /// <param name="Path">The path to update relative to <c>.codexplorer</c>.</param>
    /// <param name="Content">The text to write.</param>
    /// <param name="Mode">The update mode, either <c>replace</c> or <c>append</c>.</param>
    public sealed record Parameters(string Path, string Content, string Mode);

    Task<string> IWorkspaceTool.ExecuteAsync(JsonElement arguments, WorkspaceModel workspace, CancellationToken ct)
    {
        return this.HandleAsync(ToolRegistry.DeserializeArguments<Parameters>(arguments), workspace, ct);
    }

    /// <summary>
    /// Replaces or appends text in one existing scratch file.
    /// </summary>
    /// <param name="parameters">Typed tool arguments.</param>
    /// <param name="workspace">The workspace that constrains filesystem access.</param>
    /// <param name="ct">The cancellation token for the current tool call.</param>
    /// <returns>A success message with the workspace-relative path, or a recoverable error string.</returns>
    public async Task<string> HandleAsync(Parameters parameters, WorkspaceModel workspace, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(workspace);

        if (string.IsNullOrWhiteSpace(parameters.Path))
        {
            return "Error: path is required";
        }

        if (parameters.Content is null)
        {
            return "Error: content is required";
        }

        if (!string.Equals(parameters.Mode, "replace", StringComparison.Ordinal) &&
            !string.Equals(parameters.Mode, "append", StringComparison.Ordinal))
        {
            return "Error: mode must be either 'replace' or 'append'";
        }

        var resolvedPath = AgentScratchpad.ResolvePath(workspace, parameters.Path);
        var workspaceRelativePath = AgentScratchpad.GetWorkspaceRelativePath(workspace, resolvedPath);

        if (!File.Exists(resolvedPath))
        {
            return $"Error: file not found: {workspaceRelativePath}";
        }

        if (Directory.Exists(resolvedPath))
        {
            return $"Error: path is a directory: {workspaceRelativePath}";
        }

        if (await ToolFileHelpers.IsBinaryFileAsync(resolvedPath, ct).ConfigureAwait(false))
        {
            return $"Error: binary file, cannot write text: {workspaceRelativePath}";
        }

        if (string.Equals(parameters.Mode, "replace", StringComparison.Ordinal))
        {
            await File.WriteAllTextAsync(
                    resolvedPath,
                    parameters.Content,
                    AgentScratchpad.Utf8WithoutBom,
                    ct)
                .ConfigureAwait(false);
        }
        else
        {
            await File.AppendAllTextAsync(
                    resolvedPath,
                    parameters.Content,
                    AgentScratchpad.Utf8WithoutBom,
                    ct)
                .ConfigureAwait(false);
        }

        return $"Updated text file: {workspaceRelativePath} ({parameters.Mode})";
    }
}
