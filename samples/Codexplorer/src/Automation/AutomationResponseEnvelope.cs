namespace Codexplorer.Automation;

internal sealed record AutomationResponseEnvelope(
    string? RequestId,
    bool Success,
    object? Result,
    AutomationProtocolError? Error)
{
    public static AutomationResponseEnvelope SuccessResponse(string requestId, object? result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        return new AutomationResponseEnvelope(requestId, true, result, null);
    }

    public static AutomationResponseEnvelope ErrorResponse(string? requestId, string code, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new AutomationResponseEnvelope(requestId, false, null, new AutomationProtocolError(code, message));
    }
}
