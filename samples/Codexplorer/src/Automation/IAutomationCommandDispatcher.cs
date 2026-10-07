namespace Codexplorer.Automation;

/// <summary>Defines sequential automation command processing.</summary>
internal interface IAutomationCommandDispatcher
{
    /// <summary>Asynchronously handles one parsed protocol command.</summary>
    /// <param name="request">The parsed command and its payload.</param>
    /// <param name="ct">The work cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the command response.</returns>
    Task<AutomationResponseEnvelope> DispatchAsync(AutomationRequestEnvelope request, CancellationToken ct);
}
