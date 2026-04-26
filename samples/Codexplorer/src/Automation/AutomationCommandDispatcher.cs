namespace Codexplorer.Automation;

internal sealed class AutomationCommandDispatcher : IAutomationCommandDispatcher
{
    private static readonly HashSet<string> DeferredCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "open_session",
        "submit",
        "close_session"
    };

    public Task<AutomationResponseEnvelope> DispatchAsync(AutomationRequestEnvelope request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        return Task.FromResult(
            string.Equals(request.Command, "ping", StringComparison.OrdinalIgnoreCase)
                ? AutomationResponseEnvelope.SuccessResponse(
                    request.RequestId!,
                    new AutomationPingResult(Status: "ok", ProtocolVersion: 1))
                : DeferredCommands.Contains(request.Command!)
                    ? AutomationResponseEnvelope.ErrorResponse(
                        request.RequestId,
                        code: "not_implemented",
                        message: $"Command '{request.Command}' is recognized but not implemented yet.")
                    : AutomationResponseEnvelope.ErrorResponse(
                        request.RequestId,
                        code: "unknown_command",
                        message: $"Command '{request.Command}' is not supported."));
    }

    private sealed record AutomationPingResult(string Status, int ProtocolVersion);
}
