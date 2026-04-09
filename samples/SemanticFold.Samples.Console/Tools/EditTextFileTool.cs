using System.Text.Json;
using SemanticFold.Core.Abstractions;

namespace SemanticFold.Samples.Console.Tools;

/// <summary>
/// A tool that replaces the content of an existing text file.
/// </summary>
public sealed class EditTextFileTool : ITool
{
    public string Name => "edit_text_file";
    public string Description => "Replaces the content of an existing text file.";
    public JsonDocument? ParametersSchema => JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "filename": { "type": "string", "description": "The name of the file to edit." },
                "content": { "type": "string", "description": "The new content to write to the file." }
            },
            "required": ["filename", "content"]
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

            if (!doc.RootElement.TryGetProperty("content", out var contentProp))
            {
                return "Error: Missing 'content' argument.";
            }

            var filename = filenameProp.GetString();
            if (string.IsNullOrWhiteSpace(filename))
            {
                return "Error: 'filename' cannot be empty.";
            }

            var path = Path.GetFullPath(filename);
            if (!File.Exists(path))
            {
                return $"File not found: {filename}";
            }

            File.WriteAllText(path, contentProp.GetString() ?? string.Empty);
            return $"Updated file: {path}";
        }
        catch (Exception ex)
        {
            return $"Error editing file: {ex.Message}";
        }
    }
}
