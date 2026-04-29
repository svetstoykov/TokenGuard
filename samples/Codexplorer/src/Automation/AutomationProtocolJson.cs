using System.Text.Json;

namespace Codexplorer.Automation;

internal static class AutomationProtocolJson
{
    public static JsonSerializerOptions SerializerOptions { get; } = CreateSerializerOptions();

    public static string SerializeResponse(AutomationResponseEnvelope response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return JsonSerializer.Serialize(response, SerializerOptions);
    }

    public static bool TryParseRequest(
        string line,
        out AutomationRequestEnvelope? request,
        out AutomationResponseEnvelope? errorResponse)
    {
        ArgumentNullException.ThrowIfNull(line);

        try
        {
            request = JsonSerializer.Deserialize<AutomationRequestEnvelope>(line, SerializerOptions);
        }
        catch (JsonException ex)
        {
            request = null;
            errorResponse = AutomationResponseEnvelope.ErrorResponse(
                requestId: null,
                code: "malformed_json",
                message: $"Request line is not valid JSON. {ex.Message}");
            return false;
        }

        if (request is null)
        {
            errorResponse = AutomationResponseEnvelope.ErrorResponse(
                requestId: null,
                code: "invalid_request",
                message: "Request body must deserialize to JSON object.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.RequestId))
        {
            request = null;
            errorResponse = AutomationResponseEnvelope.ErrorResponse(
                requestId: null,
                code: "invalid_request",
                message: "Request field 'requestId' is required.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.Command))
        {
            var requestId = request.RequestId;
            request = null;
            errorResponse = AutomationResponseEnvelope.ErrorResponse(
                requestId,
                code: "invalid_request",
                message: "Request field 'command' is required.");
            return false;
        }

        errorResponse = null;
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
