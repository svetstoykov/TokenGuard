using System.Text.Json;

namespace Codexplorer.Automation;

internal sealed record AutomationRequestEnvelope
{
    public string? RequestId { get; init; }

    public string? Command { get; init; }

    public JsonElement? Payload { get; init; }
}
