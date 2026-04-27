using Codexplorer.Automation.Protocol;

namespace Codexplorer.Automation.Client;

internal interface ICodexplorerAutomationClient
{
    Task<AutomationPingResult> PingAsync(CancellationToken ct);

    Task<OpenSessionResponse> OpenSessionAsync(OpenSessionRequest request, CancellationToken ct);

    Task<SubmitResponse> SubmitAsync(SubmitRequest request, CancellationToken ct);

    Task<CloseSessionResponse> CloseSessionAsync(CloseSessionRequest request, CancellationToken ct);
}
