using System.Text.Json;
using TokenGuard.Core.Abstractions;

namespace TokenGuard.Samples.Console.Tools;

/// <summary>
/// A tool that creates a text file with optional initial content.
/// </summary>
public sealed class CreateTextFileTool : ITool
{
    public string Name => "create_text_file";
    public string Description => "Creates a text file with optional initial content.";
    public JsonDocument? ParametersSchema => JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "filename": { "type": "string", "description": "The name of the file to create." },
                "content": { "type": "string", "description": "Optional initial content for the file." }
            },
            "required": ["filename"]
        }
        """);

    public string Execute(string argumentsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            if (!doc.RootElement.TryGetProperty("filename", out var filenameProp))
            {
                return "Error: Missing 'filename' argument.";
            }

            var filename = filenameProp.GetString();
            if (string.IsNullOrWhiteSpace(filename))
            {
                return "Error: 'filename' cannot be empty.";
            }

            var path = Path.GetFullPath(filename);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var content = doc.RootElement.TryGetProperty("content", out var contentProp)
                ? contentProp.GetString() ?? string.Empty
                : string.Empty;

            File.WriteAllText(path, content);
            return $"Created file: {path}";
        }
        catch (Exception ex)
        {
            return $"Error creating file: {ex.Message}";
        }
    }
}
