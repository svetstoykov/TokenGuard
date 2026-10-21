using System.Text;
using System.Text.Json;

namespace Codexplorer.Automation.AgentComparison.Recording;

/// <summary>Provides measured tool-payload sizes from captured submitted history.</summary>
internal static class RecordedRequestDiagnostics
{
    /// <summary>Reads observed tool calls and results without inferring provider token attribution.</summary>
    /// <param name="body">The exact submitted request body.</param>
    /// <returns>The native calls visible in that request, including prior history.</returns>
    internal static IReadOnlyList<RecordedToolObservation> Read(byte[] body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var messages = document.RootElement.GetProperty("messages");
            var calls = new Dictionary<string, RecordedToolObservation>(StringComparer.Ordinal);
            var results = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var message in messages.EnumerateArray())
            {
                if (message.TryGetProperty("tool_call_id", out var resultId) && message.TryGetProperty("content", out var content))
                    results[resultId.GetString()!] = Encoding.UTF8.GetByteCount(
                        content.ValueKind == JsonValueKind.String ? content.GetString()! : content.GetRawText());
                if (!message.TryGetProperty("tool_calls", out var tools))
                    continue;
                foreach (var call in tools.EnumerateArray())
                {
                    var id = call.GetProperty("id").GetString()!;
                    var function = call.GetProperty("function");
                    var arguments = function.GetProperty("arguments");
                    calls[id] = new RecordedToolObservation(id, function.GetProperty("name").GetString()!,
                        Encoding.UTF8.GetByteCount(arguments.GetString()!), null);
                }
            }
            return calls.Values.Select(call => call with
            {
                ResultBytes = results.TryGetValue(call.CallId, out var size) ? size : null
            }).ToArray();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentNullException)
        {
            return [];
        }
    }
}
