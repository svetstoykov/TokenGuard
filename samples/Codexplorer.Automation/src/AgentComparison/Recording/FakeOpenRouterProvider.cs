using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace Codexplorer.Automation.AgentComparison.Recording;

/// <summary>Represents a scripted assistant tool call returned by the fake provider.</summary>
/// <param name="Id">The stable tool-call identifier.</param>
/// <param name="Name">The tool name requested by the assistant.</param>
/// <param name="ArgumentsJson">The serialized JSON arguments.</param>
internal sealed record FakeToolCall(string Id, string Name, string ArgumentsJson);

/// <summary>Represents one deterministic chat completion response.</summary>
/// <param name="Content">The assistant text content.</param>
/// <param name="ToolCalls">The tool calls requested by the assistant.</param>
/// <param name="Usage">The provider usage object returned with the response.</param>
/// <param name="GenerationId">The stable generation identifier.</param>
/// <param name="StatusCode">The scripted provider status code.</param>
/// <param name="RawBody">An optional exact response body override.</param>
/// <param name="ContentType">An optional response media type override.</param>
internal sealed record FakeCompletion(
    string? Content,
    IReadOnlyList<FakeToolCall> ToolCalls,
    JsonElement Usage,
    string GenerationId,
    int StatusCode = 200,
    byte[]? RawBody = null,
    string? ContentType = null);

/// <summary>Provides deterministic OpenRouter-compatible JSON and server-sent event responses.</summary>
internal sealed class FakeOpenRouterProvider
{
    private readonly ConcurrentQueue<FakeCompletion> _script;
    private readonly ConcurrentQueue<byte[]> _requests = new();
    private readonly Func<JsonElement, FakeCompletion>? _requestResponder;

    /// <summary>Initializes a scripted provider with responses consumed in request order.</summary>
    /// <param name="script">The completion sequence to return.</param>
    public FakeOpenRouterProvider(IEnumerable<FakeCompletion> script)
    {
        this._script = new ConcurrentQueue<FakeCompletion>(script);
    }

    /// <summary>Initializes a fake provider whose response depends on the received request shape.</summary>
    /// <param name="requestResponder">A deterministic callback invoked with the parsed request object.</param>
    public FakeOpenRouterProvider(Func<JsonElement, FakeCompletion> requestResponder)
    {
        ArgumentNullException.ThrowIfNull(requestResponder);
        this._script = new ConcurrentQueue<FakeCompletion>();
        this._requestResponder = requestResponder;
    }

    /// <summary>Gets exact request bodies received by the fake provider.</summary>
    internal IReadOnlyList<byte[]> Requests => this._requests.ToArray();

    /// <summary>Records a request and returns the next scripted completion.</summary>
    /// <param name="requestBody">The exact effective request bytes.</param>
    /// <returns>A deterministic JSON or event-stream response.</returns>
    internal FakeProviderResponse Respond(ReadOnlyMemory<byte> requestBody)
    {
        this._requests.Enqueue(requestBody.ToArray());
        using var document = JsonDocument.Parse(requestBody);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException("A chat completion request must be a JSON object.");
        var model = root.TryGetProperty("model", out var modelElement) && modelElement.ValueKind == JsonValueKind.String
            ? modelElement.GetString() ?? "unknown"
            : "unknown";
        var stream = root.TryGetProperty("stream", out var streamElement) && streamElement.ValueKind == JsonValueKind.True;
        var includeUsage = root.TryGetProperty("stream_options", out var streamOptions)
            && streamOptions.TryGetProperty("include_usage", out var includeUsageElement)
            && includeUsageElement.ValueKind == JsonValueKind.True;
        if (this._requestResponder is not null)
        {
            var scriptedCompletion = this._requestResponder(root);
            var responseBody = scriptedCompletion.RawBody ?? (stream
                ? BuildEventStream(scriptedCompletion, model, includeUsage)
                : BuildJson(scriptedCompletion, model));
            return new FakeProviderResponse(responseBody,
                scriptedCompletion.ContentType ?? (stream ? "text/event-stream" : "application/json"),
                (System.Net.HttpStatusCode)scriptedCompletion.StatusCode);
        }

        if (!this._script.TryDequeue(out var completion))
            completion = new FakeCompletion("Synthetic response", [], EmptyUsage(), $"fake-{this._requests.Count}");

        var body = completion.RawBody ?? (stream ? BuildEventStream(completion, model, includeUsage) : BuildJson(completion, model));
        return new FakeProviderResponse(body, completion.ContentType ?? (stream ? "text/event-stream" : "application/json"),
            (System.Net.HttpStatusCode)completion.StatusCode);
    }

    private static byte[] BuildJson(FakeCompletion completion, string model)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("id", completion.GenerationId);
            writer.WriteString("object", "chat.completion");
            writer.WriteString("model", model);
            writer.WriteStartArray("choices");
            writer.WriteStartObject();
            writer.WriteNumber("index", 0);
            writer.WriteStartObject("message");
            writer.WriteString("role", "assistant");
            if (completion.Content is null)
                writer.WriteNull("content");
            else
                writer.WriteString("content", completion.Content);
            if (completion.ToolCalls.Count > 0)
            {
                writer.WriteStartArray("tool_calls");
                foreach (var call in completion.ToolCalls)
                {
                    writer.WriteStartObject();
                    writer.WriteString("id", call.Id);
                    writer.WriteString("type", "function");
                    writer.WriteStartObject("function");
                    writer.WriteString("name", call.Name);
                    writer.WriteString("arguments", call.ArgumentsJson);
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }
            writer.WriteEndObject();
            writer.WriteString("finish_reason", completion.ToolCalls.Count > 0 ? "tool_calls" : "stop");
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WritePropertyName("usage");
            completion.Usage.WriteTo(writer);
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static byte[] BuildEventStream(FakeCompletion completion, string model, bool includeUsage)
    {
        var frames = new List<string>();
        var delta = new Dictionary<string, object?> { ["role"] = "assistant" };
        if (completion.Content is not null)
            delta["content"] = completion.Content;
        if (completion.ToolCalls.Count > 0)
            delta["tool_calls"] = completion.ToolCalls.Select((call, index) => new
            {
                index,
                id = call.Id,
                type = "function",
                function = new { name = call.Name, arguments = call.ArgumentsJson },
            });

        frames.Add(JsonSerializer.Serialize(new
        {
            id = completion.GenerationId,
            @object = "chat.completion.chunk",
            model,
            choices = new[] { new { index = 0, delta, finish_reason = (string?)null } },
        }));
        frames.Add(JsonSerializer.Serialize(new
        {
            id = completion.GenerationId,
            @object = "chat.completion.chunk",
            model,
            choices = new[] { new { index = 0, delta = new { }, finish_reason = completion.ToolCalls.Count > 0 ? "tool_calls" : "stop" } },
        }));
        if (includeUsage)
            frames.Add(JsonSerializer.Serialize(new
            {
                id = completion.GenerationId,
                @object = "chat.completion.chunk",
                model,
                choices = Array.Empty<object>(),
                usage = completion.Usage,
            }));
        frames.Add("[DONE]");
        return Encoding.UTF8.GetBytes(string.Concat(frames.Select(frame => $"data: {frame}\n\n")));
    }

    private static JsonElement EmptyUsage()
    {
        using var document = JsonDocument.Parse("{\"prompt_tokens\":0,\"completion_tokens\":0,\"total_tokens\":0}");
        return document.RootElement.Clone();
    }
}

/// <summary>Represents a fake provider body and its response media type.</summary>
/// <param name="Body">The exact response bytes.</param>
/// <param name="ContentType">The response media type.</param>
/// <param name="Status">The scripted response status.</param>
internal sealed record FakeProviderResponse(byte[] Body, string ContentType, System.Net.HttpStatusCode Status);
