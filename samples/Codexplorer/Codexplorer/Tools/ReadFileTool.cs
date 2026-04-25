using System.Text.Json;
using WorkspaceModel = Codexplorer.Workspace.Workspace;

namespace Codexplorer.Tools;

/// <summary>
/// Reads one text file from start to finish with a hard line cap.
/// </summary>
/// <remarks>
/// Use this when the full contents of one likely-small text file are needed. For large files or when
/// only one section matters, <see cref="ReadRangeTool"/> is cheaper and should be preferred.
/// </remarks>
public sealed class ReadFileTool : IWorkspaceTool
{
    /// <summary>
    /// Maximum number of lines returned by one call.
    /// </summary>
    public const int LineCap = 2000;

    private static readonly ToolSchema CachedSchema = ToolSchema.CreateFunction(
        "read_file",
        "Read full text of one workspace-relative file, capped at 2000 lines. Prefer this for smaller files when complete contents matter; use read_range for large files or focused inspection.",
        """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "path": {
              "type": "string",
              "description": "Workspace-relative file path to read fully."
            }
          },
          "required": ["path"]
        }
        """);

    /// <summary>
    /// Gets tool name exposed to the model.
    /// </summary>
    public string Name => "read_file";

    /// <summary>
    /// Gets cached OpenAI-compatible schema for this tool.
    /// </summary>
    public ToolSchema Schema => CachedSchema;

    /// <summary>
    /// Represents arguments for <see cref="ReadFileTool"/>.
    /// </summary>
    /// <param name="Path">The workspace-relative file path to read.</param>
    public sealed record Parameters(string Path);

    Task<string> IWorkspaceTool.ExecuteAsync(JsonElement arguments, WorkspaceModel workspace, CancellationToken ct)
    {
        return this.HandleAsync(ToolRegistry.DeserializeArguments<Parameters>(arguments), workspace, ct);
    }

    /// <summary>
    /// Reads one file as text until EOF or <see cref="LineCap"/> is reached.
    /// </summary>
    /// <param name="parameters">Typed tool arguments.</param>
    /// <param name="workspace">The workspace that constrains file access.</param>
    /// <param name="ct">The cancellation token for the current tool call.</param>
    /// <returns>The file text, a truncation marker, or a recoverable error string.</returns>
    public async Task<string> HandleAsync(Parameters parameters, WorkspaceModel workspace, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(workspace);

        var requestedPath = string.IsNullOrWhiteSpace(parameters.Path) ? "." : parameters.Path;
        var resolvedPath = PathGuard.ResolvePath(workspace.LocalPath, requestedPath);

        if (!File.Exists(resolvedPath))
        {
            return $"Error: file not found: {ToolResultFormatting.NormalizePath(requestedPath)}";
        }

        if (Directory.Exists(resolvedPath))
        {
            return $"Error: path is a directory: {ToolResultFormatting.NormalizePath(requestedPath)}";
        }

        if (await ToolFileHelpers.IsBinaryFileAsync(resolvedPath, ct).ConfigureAwait(false))
        {
            return "Error: binary file, cannot display as text";
        }

        var lines = new List<string>(Math.Min(LineCap, 256));
        var totalLineCount = 0;

        using var stream = new FileStream(
            resolvedPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 4096,
            useAsync: true);

        using var reader = new StreamReader(stream);

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);

            if (line is null)
            {
                break;
            }

            totalLineCount++;

            if (lines.Count < LineCap)
            {
                lines.Add(line);
            }
        }

        return ToolFileHelpers.BuildTextResult(lines, totalLineCount, LineCap);
    }
}
