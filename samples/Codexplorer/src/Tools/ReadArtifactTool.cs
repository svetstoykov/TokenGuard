using System.Text.Json;

namespace Codexplorer.Tools;

/// <summary>
///     Reads one file from the session's artifacts folder, optionally limited to a line range.
/// </summary>
/// <remarks>
///     This is how the agent checks what it wrote earlier. It resolves a path exactly as the tools that write do, so a
///     file just created is always readable under the same path.
/// </remarks>
public sealed class ReadArtifactTool : IArtifactTool
{
    /// <summary>
    ///     The maximum number of lines returned by one call.
    /// </summary>
    public const int LineCap = 2000;

    private static readonly ToolSchema CachedSchema = ToolSchema.CreateFunction(
        "read_artifact",
        "Read one text file from your artifacts folder, capped at 2000 lines. Omit startLine and endLine to read the whole file, or give both to read a line range.",
        """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "path": {
              "type": "string",
              "description": "Path relative to the artifacts folder, such as `report.md` or `notes/summary.md`."
            },
            "startLine": {
              "type": "integer",
              "description": "Optional 1-based inclusive start line."
            },
            "endLine": {
              "type": "integer",
              "description": "Optional 1-based inclusive end line."
            }
          },
          "required": ["path"]
        }
        """);

    /// <summary>
    ///     Gets the tool name exposed to the model.
    /// </summary>
    public string Name => "read_artifact";

    /// <summary>
    ///     Gets the cached OpenAI-compatible schema for this tool.
    /// </summary>
    public ToolSchema Schema => CachedSchema;

    /// <summary>
    ///     Represents the arguments of <see cref="ReadArtifactTool" />.
    /// </summary>
    /// <param name="Path">The path to read, relative to the artifacts folder.</param>
    /// <param name="StartLine">The 1-based inclusive start line, or <see langword="null" /> to start at the first line.</param>
    /// <param name="EndLine">The 1-based inclusive end line, or <see langword="null" /> to read to the end of the file.</param>
    public sealed record Parameters(string Path, int? StartLine, int? EndLine);

    Task<string> IArtifactTool.ExecuteAsync(JsonElement arguments, string artifactsDirectory, CancellationToken ct)
    {
        return this.HandleAsync(ToolRegistry.DeserializeArguments<Parameters>(arguments), artifactsDirectory, ct);
    }

    /// <summary>
    ///     Asynchronously reads one artifact file, or the requested line range of it.
    /// </summary>
    /// <param name="parameters">The typed tool arguments. Cannot be <see langword="null" />.</param>
    /// <param name="artifactsDirectory">The absolute path of the session's artifacts folder.</param>
    /// <param name="ct">A <see cref="CancellationToken" /> to observe while waiting for the task to complete.</param>
    /// <returns>
    ///     A task that represents the asynchronous operation. The task result contains the requested text, a truncation
    ///     marker when the line cap is hit, or a recoverable error string.
    /// </returns>
    public async Task<string> HandleAsync(Parameters parameters, string artifactsDirectory, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var startLine = parameters.StartLine ?? 1;
        var endLine = parameters.EndLine ?? int.MaxValue;

        if (startLine < 1 || endLine < startLine)
        {
            return "Error: invalid range: startLine must be >= 1 and endLine must be >= startLine";
        }

        if (!ArtifactPaths.TryResolve(artifactsDirectory, parameters.Path, out var resolvedPath, out var artifactPath, out var error))
        {
            return error;
        }

        if (Directory.Exists(resolvedPath))
        {
            return $"Error: path is a directory: {artifactPath}";
        }

        if (!File.Exists(resolvedPath))
        {
            return $"Error: file not found: {artifactPath}";
        }

        var lines = new List<string>(256);
        var matchedLineCount = 0;
        var currentLineNumber = 0;

        using var stream = new FileStream(resolvedPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 4096, useAsync: true);
        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            currentLineNumber++;

            if (currentLineNumber < startLine)
            {
                continue;
            }

            if (currentLineNumber > endLine)
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
