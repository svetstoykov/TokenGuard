using System.Text.Json;
using WorkspaceModel = Codexplorer.Workspace.Workspace;

namespace Codexplorer.Tools;

/// <summary>
/// Reads one line range from one text file with a hard cap.
/// </summary>
/// <remarks>
/// Use this when only one section of a file matters or when a file may be large. Compared with
/// <see cref="ReadFileTool"/>, this keeps observation cost lower by returning only requested lines.
/// </remarks>
public sealed class ReadRangeTool : IWorkspaceTool
{
    /// <summary>
    /// Maximum number of lines returned by one call.
    /// </summary>
    public const int LineCap = 2000;

    private static readonly ToolSchema CachedSchema = ToolSchema.CreateFunction(
        "read_range",
        "Read one line range from one workspace-relative text file, capped at 2000 lines. Prefer this over read_file when file might be large or only one section is needed.",
        """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "path": {
              "type": "string",
              "description": "Workspace-relative file path to read."
            },
            "startLine": {
              "type": "integer",
              "description": "1-based inclusive start line."
            },
            "endLine": {
              "type": "integer",
              "description": "1-based inclusive end line."
            }
          },
          "required": ["path", "startLine", "endLine"]
        }
        """);

    /// <summary>
    /// Gets tool name exposed to the model.
    /// </summary>
    public string Name => "read_range";

    /// <summary>
    /// Gets cached OpenAI-compatible schema for this tool.
    /// </summary>
    public ToolSchema Schema => CachedSchema;

    /// <summary>
    /// Represents arguments for <see cref="ReadRangeTool"/>.
    /// </summary>
    /// <param name="Path">The workspace-relative file path to read.</param>
    /// <param name="StartLine">The 1-based inclusive start line.</param>
    /// <param name="EndLine">The 1-based inclusive end line.</param>
    public sealed record Parameters(string Path, int StartLine, int EndLine);

    Task<string> IWorkspaceTool.ExecuteAsync(JsonElement arguments, WorkspaceModel workspace, CancellationToken ct)
    {
        return this.HandleAsync(ToolRegistry.DeserializeArguments<Parameters>(arguments), workspace, ct);
    }

    /// <summary>
    /// Reads one requested line range from one file.
    /// </summary>
    /// <param name="parameters">Typed tool arguments.</param>
    /// <param name="workspace">The workspace that constrains file access.</param>
    /// <param name="ct">The cancellation token for the current tool call.</param>
    /// <returns>The requested line range, a truncation marker, or a recoverable error string.</returns>
    public async Task<string> HandleAsync(Parameters parameters, WorkspaceModel workspace, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(workspace);

        if (parameters.StartLine < 1 || parameters.EndLine < parameters.StartLine)
        {
            return "Error: invalid range: startLine must be >= 1 and endLine must be >= startLine";
        }

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
        var matchedLineCount = 0;
        var currentLineNumber = 0;

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

            currentLineNumber++;

            if (currentLineNumber < parameters.StartLine)
            {
                continue;
            }

            if (currentLineNumber > parameters.EndLine)
            {
                break;
            }

            matchedLineCount++;

            if (lines.Count < LineCap)
            {
                lines.Add(line);
            }
        }

        return ToolFileHelpers.BuildTextResult(lines, matchedLineCount, LineCap);
    }
}
