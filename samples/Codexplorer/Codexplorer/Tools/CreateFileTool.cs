using System.Text.Json;
using WorkspaceModel = Codexplorer.Workspace.Workspace;

namespace Codexplorer.Tools;

/// <summary>
/// Creates one new text file under the agent-owned scratch directory.
/// </summary>
/// <remarks>
/// This tool gives the model a safe place to persist notes and intermediate artifacts without
/// mutating repository files. The target path is always resolved under <c>.codexplorer</c>.
/// </remarks>
public sealed class CreateFileTool : IWorkspaceTool
{
    private static readonly ToolSchema CachedSchema = ToolSchema.CreateFunction(
        "create_file",
        "Create one new UTF-8 text file under the agent-owned scratch directory `.codexplorer`. Use this for notes or intermediate artifacts the agent owns. Fails if the file already exists.",
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
              "description": "Optional initial text written into the file when it is created."
            }
          },
          "required": ["path"]
        }
        """);

    /// <summary>
    /// Gets tool name exposed to the model.
    /// </summary>
    public string Name => "create_file";

    /// <summary>
    /// Gets cached OpenAI-compatible schema for this tool.
    /// </summary>
    public ToolSchema Schema => CachedSchema;

    /// <summary>
    /// Represents arguments for <see cref="CreateFileTool"/>.
    /// </summary>
    /// <param name="Path">The path to create relative to <c>.codexplorer</c>.</param>
    /// <param name="Content">Optional initial text content.</param>
    public sealed record Parameters(string Path, string? Content);

    Task<string> IWorkspaceTool.ExecuteAsync(JsonElement arguments, WorkspaceModel workspace, CancellationToken ct)
    {
        return this.HandleAsync(ToolRegistry.DeserializeArguments<Parameters>(arguments), workspace, ct);
    }

    /// <summary>
    /// Creates a new scratch text file and optionally seeds its initial content.
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

        var resolvedPath = AgentScratchpad.ResolvePath(workspace, parameters.Path);
        var workspaceRelativePath = AgentScratchpad.GetWorkspaceRelativePath(workspace, resolvedPath);

        if (Directory.Exists(resolvedPath))
        {
            return $"Error: path is a directory: {workspaceRelativePath}";
        }

        if (File.Exists(resolvedPath))
        {
            return $"Error: file already exists: {workspaceRelativePath}";
        }

        AgentScratchpad.EnsureParentDirectoryExists(resolvedPath);
        await File.WriteAllTextAsync(
                resolvedPath,
                parameters.Content ?? string.Empty,
                AgentScratchpad.Utf8WithoutBom,
                ct)
            .ConfigureAwait(false);

        return $"Created text file: {workspaceRelativePath}";
    }
}
