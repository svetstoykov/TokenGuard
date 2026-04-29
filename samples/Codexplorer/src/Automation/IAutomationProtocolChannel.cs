namespace Codexplorer.Automation;

internal interface IAutomationProtocolChannel : IAsyncDisposable
{
    ValueTask<string?> ReadLineAsync(CancellationToken ct);

    ValueTask WriteResponseAsync(AutomationResponseEnvelope response, CancellationToken ct);
}
