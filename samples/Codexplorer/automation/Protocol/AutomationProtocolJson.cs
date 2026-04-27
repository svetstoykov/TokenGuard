using System.Text.Json;

namespace Codexplorer.Automation.Protocol;

internal static class AutomationProtocolJson
{
    public static JsonSerializerOptions SerializerOptions { get; } = CreateSerializerOptions();

    public static string SerializeRequest(AutomationRequestEnvelope request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return JsonSerializer.Serialize(request, SerializerOptions);
    }

    public static bool TryParseResponse(
        string line,
        out AutomationResponseEnvelope? response,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(line);

        try
        {
            response = JsonSerializer.Deserialize<AutomationResponseEnvelope>(line, SerializerOptions);
        }
        catch (JsonException ex)
        {
            response = null;
            error = $"Codexplorer returned malformed JSON on stdout. {ex.Message}";
            return false;
        }

        if (response is null)
        {
            error = "Codexplorer returned an empty protocol response.";
            return false;
        }

        error = null;
        return true;
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        return new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = false
        };
    }
}
