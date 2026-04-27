using System.Text.Json;

namespace Codexplorer.Automation.Protocol;

internal sealed record AutomationResponseEnvelope(
    string? RequestId,
    bool Success,
    JsonElement? Result,
    AutomationProtocolError? Error);
