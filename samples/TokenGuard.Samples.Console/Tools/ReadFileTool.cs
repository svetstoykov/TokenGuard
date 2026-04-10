using System.Text.Json;
using TokenGuard.Core.Abstractions;

namespace TokenGuard.Samples.Console.Tools;

/// <summary>
/// A tool that reads the content of a specified text file.
/// </summary>
public sealed class ReadFileTool : ITool
{
    public string Name => "read_file";
    public string Description => "Reads the content of a specified text file.";
    public JsonDocument? ParametersSchema => JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "filename": { "type": "string", "description": "The name of the file to read." }
            },
            "required": ["filename"]
        }
        """);

    public string Execute(string argumentsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            if (doc.RootElement.TryGetProperty("filename", out var filenameProp))
            {
                var filename = filenameProp.GetString()!;
                var path = Path.GetFullPath(filename);
                if (File.Exists(path))
                {
                    return File.ReadAllText(path);
                }
                return $"File not found: {filename}";
            }
            return "Error: Missing 'filename' argument.";
        }
        catch (Exception ex)
        {
            return $"Error reading file: {ex.Message}";
        }
    }
}
