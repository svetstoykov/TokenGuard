using System.Text;
using System.Text.Json;

namespace Codexplorer.Tools;

/// <summary>
///     Lists the files written so far in the session's artifacts folder.
/// </summary>
/// <remarks>
///     After compaction the agent may not remember what it wrote. Listing shows which files exist, so it can decide
///     between creating a new file and writing to an existing one.
/// </remarks>
public sealed class ListArtifactsTool : IArtifactTool
{
    private static readonly ToolSchema CachedSchema = ToolSchema.CreateFunction(
        "list_artifacts",
        "List every file you have written so far in your artifacts folder, with its size. Use this to check what exists before creating or writing a file.",
        """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {}
        }
        """);

    /// <summary>
    ///     Gets the tool name exposed to the model.
    /// </summary>
    public string Name => "list_artifacts";

    /// <summary>
    ///     Gets the cached OpenAI-compatible schema for this tool.
    /// </summary>
    public ToolSchema Schema => CachedSchema;

    Task<string> IArtifactTool.ExecuteAsync(JsonElement arguments, string artifactsDirectory, CancellationToken ct)
    {
        return Task.FromResult(this.Handle(artifactsDirectory));
    }

    /// <summary>
    ///     Lists the artifact files with their artifact-relative paths and sizes.
    /// </summary>
    /// <param name="artifactsDirectory">The absolute path of the session's artifacts folder.</param>
    /// <returns>One line per file ordered by path, or a note that nothing has been written yet.</returns>
    public string Handle(string artifactsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactsDirectory);

        if (!Directory.Exists(artifactsDirectory))
        {
            return "No artifacts have been written yet.";
        }

        var files = Directory.EnumerateFiles(artifactsDirectory, "*", SearchOption.AllDirectories)
            .Select(file => (Path: ArtifactPaths.ToArtifactPath(artifactsDirectory, file), Size: new FileInfo(file).Length))
            .OrderBy(static file => file.Path, StringComparer.Ordinal)
            .ToArray();

        if (files.Length == 0)
        {
            return "No artifacts have been written yet.";
        }

        var builder = new StringBuilder();
        builder.AppendJoin(Environment.NewLine, files.Select(static file => $"{file.Path} ({file.Size} bytes)"));
        return builder.ToString();
    }
}
