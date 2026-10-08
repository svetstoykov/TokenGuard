using System.Text.Json;

namespace Codexplorer.Tools;

/// <summary>
///     Creates one new text file in the session's artifacts folder.
/// </summary>
/// <remarks>
///     The artifacts folder is the only place the agent writes. Creating a file that already exists fails and leaves it
///     unchanged, so an agent that lost track of its own output after compaction cannot overwrite it by accident.
/// </remarks>
public sealed class CreateArtifactTool : IArtifactTool
{
    private static readonly ToolSchema CachedSchema = ToolSchema.CreateFunction(
        "create_artifact",
        "Create one new UTF-8 text file in your artifacts folder, the only place you can write. Use this for notes and deliverables. Fails without changing anything if the file already exists.",
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
              "description": "Optional initial text written into the file when it is created."
            }
          },
          "required": ["path"]
        }
        """);

    /// <summary>
    ///     Gets the tool name exposed to the model.
    /// </summary>
    public string Name => "create_artifact";

    /// <summary>
    ///     Gets the cached OpenAI-compatible schema for this tool.
    /// </summary>
    public ToolSchema Schema => CachedSchema;

    /// <summary>
    ///     Represents the arguments of <see cref="CreateArtifactTool" />.
    /// </summary>
    /// <param name="Path">The path to create, relative to the artifacts folder.</param>
    /// <param name="Content">The initial text, or <see langword="null" /> to create an empty file.</param>
    public sealed record Parameters(string Path, string? Content);

    Task<string> IArtifactTool.ExecuteAsync(JsonElement arguments, string artifactsDirectory, CancellationToken ct)
    {
        return this.HandleAsync(ToolRegistry.DeserializeArguments<Parameters>(arguments), artifactsDirectory, ct);
    }

    /// <summary>
    ///     Asynchronously creates a new artifact file and writes its initial content.
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

        if (!ArtifactPaths.TryResolve(artifactsDirectory, parameters.Path, out var resolvedPath, out var artifactPath, out var error))
        {
            return error;
        }

        if (Directory.Exists(resolvedPath))
        {
            return $"Error: path is a directory: {artifactPath}";
        }

        if (File.Exists(resolvedPath))
        {
            return $"Error: file already exists: {artifactPath}";
        }

        Directory.CreateDirectory(Path.GetDirectoryName(resolvedPath)!);
        await File.WriteAllTextAsync(resolvedPath, parameters.Content ?? string.Empty, ArtifactPaths.Utf8WithoutBom, ct).ConfigureAwait(false);

        return $"Created artifact: {artifactPath}";
    }
}
