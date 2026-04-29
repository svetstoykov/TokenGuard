namespace Codexplorer.Automation;

internal interface IAutomationCommandDispatcher
{
    Task<AutomationResponseEnvelope> DispatchAsync(AutomationRequestEnvelope request, CancellationToken ct);
}
