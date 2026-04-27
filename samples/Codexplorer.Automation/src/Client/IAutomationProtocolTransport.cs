using Codexplorer.Automation.Protocol;

namespace Codexplorer.Automation.Client;

internal interface IAutomationProtocolTransport : IAsyncDisposable
{
    int? ProcessId { get; }

    Task StartAsync(CancellationToken ct);

    Task<AutomationResponseEnvelope> SendAsync(AutomationRequestEnvelope request, CancellationToken ct);
}
