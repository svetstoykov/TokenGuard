using System.Text.Json;

namespace TokenGuard.Tools.Tools;

/// <summary>
/// Lists files within a configured workspace directory.
/// </summary>
/// <remarks>
/// The tool constrains enumeration to a test-owned workspace so live E2E runs can exercise genuine tool calls
/// without touching unrelated repository files.
/// </remarks>
public sealed class ListFilesTool(string workspaceDirectory) : ITool
{
    /// <summary>
    /// Gets the tool name.
    /// </summary>
    public string Name => "list_files";

    /// <summary>
    /// Gets the tool description.
    /// </summary>
    public string Description => "Lists all files in the current task workspace.";

    /// <summary>
    /// Gets the tool schema.
    /// </summary>
    public JsonDocument? ParametersSchema => null;

    /// <summary>
    /// Lists files in the workspace.
    /// </summary>
    /// <param name="argumentsJson">Unused JSON payload supplied by the model provider.</param>
    /// <returns>A newline-delimited file list, or a message when the workspace is empty.</returns>
    public string Execute(string argumentsJson)
    {
        _ = argumentsJson;

        try
        {
            var files = Directory.GetFiles(workspaceDirectory)
                .Select(Path.GetFileName)
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToArray();

            return files.Length > 0 ? string.Join("\n", files) : "Directory is empty.";
        }
        catch (Exception ex)
        {
            return $"Error listing files: {ex.Message}";
        }
    }
}
