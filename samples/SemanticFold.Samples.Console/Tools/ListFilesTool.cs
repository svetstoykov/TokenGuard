using System.Text.Json;
using SemanticFold.Core.Abstractions;

namespace SemanticFold.Samples.Console.Tools;

/// <summary>
/// A tool that lists all files in the current working directory.
/// </summary>
public sealed class ListFilesTool : ITool
{
    public string Name => "list_files";
    public string Description => "Lists all files in the current working directory.";
    public JsonDocument? ParametersSchema => null;

    public string Execute(string argumentsJson)
    {
        try
        {
            var dir = Directory.GetCurrentDirectory();
            var files = Directory.GetFiles(dir).Select(Path.GetFileName).ToArray();
            return files.Length > 0 ? string.Join("\n", files) : "Directory is empty.";
        }
        catch (Exception ex)
        {
            return $"Error listing files: {ex.Message}";
        }
    }
}
