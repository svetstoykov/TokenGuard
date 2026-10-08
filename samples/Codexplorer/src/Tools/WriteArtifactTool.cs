using System.Text.Json;

namespace Codexplorer.Tools;

/// <summary>
///     Replaces or appends text in an existing file in the session's artifacts folder.
/// </summary>
/// <remarks>
///     Writing only works on a file that already exists, which keeps creating and updating two explicit steps.
/// </remarks>
public sealed class WriteArtifactTool : IArtifactTool
{
    private static readonly ToolSchema CachedSchema = ToolSchema.CreateFunction(
        "write_artifact",
        "Write UTF-8 text into an existing file in your artifacts folder. Use mode `replace` to overwrite the file or `append` to add more text. Fails if the file does not exist.",
        """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "path": {
              "type": "string",
              "description": "Path relative to the artifacts folder, such as `report.md` or `notes/summary.md`."
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
    ///     Gets the tool name exposed to the model.
    /// </summary>
    public string Name => "write_artifact";

    /// <summary>
    ///     Gets the cached OpenAI-compatible schema for this tool.
    /// </summary>
    public ToolSchema Schema => CachedSchema;

    /// <summary>
    ///     Represents the arguments of <see cref="WriteArtifactTool" />.
    /// </summary>
    /// <param name="Path">The path to update, relative to the artifacts folder.</param>
    /// <param name="Content">The text to write.</param>
    /// <param name="Mode">The update mode, either <c>replace</c> or <c>append</c>.</param>
    public sealed record Parameters(string Path, string Content, string Mode);

    Task<string> IArtifactTool.ExecuteAsync(JsonElement arguments, string artifactsDirectory, CancellationToken ct)
    {
        return this.HandleAsync(ToolRegistry.DeserializeArguments<Parameters>(arguments), artifactsDirectory, ct);
    }

    /// <summary>
    ///     Asynchronously replaces or appends text in one existing artifact file.
    /// </summary>
    /// <param name="parameters">The typed tool arguments. Cannot be <see langword="null" />.</param>
    /// <param name="artifactsDirectory">The absolute path of the session's artifacts folder.</param>
    /// <param name="ct">A <see cref="CancellationToken" /> to observe while waiting for the task to complete.</param>
    /// <returns>
    ///     A task that represents the asynchronous operation. The task result contains a success message with the
    ///     artifact-relative path, or a recoverable error string.
    /// </returns>
    public async Task<string> HandleAsync(Parameters parameters, string artifactsDirectory, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        if (parameters.Content is null)
        {
            return "Error: content is required";
        }

        if (parameters.Mode is not ("replace" or "append"))
        {
            return "Error: mode must be either 'replace' or 'append'";
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

        if (parameters.Mode == "replace")
        {
            await File.WriteAllTextAsync(resolvedPath, parameters.Content, ArtifactPaths.Utf8WithoutBom, ct).ConfigureAwait(false);
        }
        else
        {
            await File.AppendAllTextAsync(resolvedPath, parameters.Content, ArtifactPaths.Utf8WithoutBom, ct).ConfigureAwait(false);
        }

        return $"Updated artifact: {artifactPath} ({parameters.Mode})";
    }
}
