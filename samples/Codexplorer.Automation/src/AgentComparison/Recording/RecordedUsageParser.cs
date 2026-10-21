using System.Text.Json;

namespace Codexplorer.Automation.AgentComparison.Recording;

/// <summary>Extracts terminal usage and generation metadata from provider responses.</summary>
internal static class RecordedUsageParser
{
    /// <summary>Parses one JSON or server-sent event response body.</summary>
    /// <param name="body">The exact response bytes.</param>
    /// <param name="contentType">The response media type.</param>
    /// <returns>The normalized usage, raw usage JSON, and generation identifier when available.</returns>
    internal static (RecordedUsage? Usage, string? RawJson, string? GenerationId) Parse(byte[] body, string contentType)
    {
        JsonElement usage = default;
        string? generationId = null;
        try
        {
            if (contentType.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase))
            {
                var text = System.Text.Encoding.UTF8.GetString(body).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
                foreach (var frame in text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
                {
                    var data = frame.Split('\n').Where(line => line.StartsWith("data:", StringComparison.Ordinal)).ToArray();
                    if (data.Length == 0)
                        continue;
                    var payload = string.Join("\n", data.Select(line => line[5..].TrimStart()));
                    if (payload == "[DONE]")
                        continue;
                    using var eventDocument = JsonDocument.Parse(payload);
                    var eventRoot = eventDocument.RootElement;
                    if (eventRoot.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                        generationId = id.GetString();
                    if (eventRoot.TryGetProperty("usage", out var eventUsage) && eventUsage.ValueKind == JsonValueKind.Object)
                        usage = eventUsage.Clone();
                }
            }
            else
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                if (root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                    generationId = id.GetString();
                if (root.TryGetProperty("usage", out var responseUsage) && responseUsage.ValueKind == JsonValueKind.Object)
                    usage = responseUsage.Clone();
            }

            if (usage.ValueKind != JsonValueKind.Object)
                return (null, null, generationId);
            var normalized = Normalize(usage);
            if (contentType.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase)
                && !System.Text.Encoding.UTF8.GetString(body).Contains("[DONE]", StringComparison.Ordinal))
                normalized = normalized with { Complete = false };
            return (normalized, usage.GetRawText(), generationId);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return (null, null, generationId);
        }
    }

    private static RecordedUsage Normalize(JsonElement usage)
    {
        var input = ReadLong(usage, "prompt_tokens") ?? ReadLong(usage, "input_tokens");
        var output = ReadLong(usage, "completion_tokens") ?? ReadLong(usage, "output_tokens");
        var cached = ReadNestedLong(usage, "prompt_tokens_details", "cached_tokens")
            ?? ReadNestedLong(usage, "input_tokens_details", "cached_tokens");
        var reasoning = ReadNestedLong(usage, "completion_tokens_details", "reasoning_tokens")
            ?? ReadNestedLong(usage, "output_tokens_details", "reasoning_tokens");
        var cacheWrite = ReadLong(usage, "cache_write_tokens")
            ?? ReadNestedLong(usage, "prompt_tokens_details", "cache_write_tokens")
            ?? ReadLong(usage, "cache_creation_input_tokens");
        var cost = ReadDecimal(usage, "cost");
        return new RecordedUsage(input, output, cached, reasoning, cacheWrite, cost, input.HasValue && output.HasValue);
    }

    private static long? ReadNestedLong(JsonElement element, string parent, string child) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(parent, out var nested) ? ReadLong(nested, child) : null;

    private static long? ReadLong(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var result) ? result : null;

    private static decimal? ReadDecimal(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var result) ? result : null;
}
