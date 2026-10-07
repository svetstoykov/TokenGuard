using Codexplorer.Automation.Protocol;

namespace Codexplorer.Automation.Client;

/// <summary>Defines the subprocess transport boundary.</summary>
internal interface IAutomationProtocolTransport : IAsyncDisposable
{
    /// <summary>Gets the active child process identifier.</summary>
    int? ProcessId { get; }

    /// <summary>Asynchronously starts the child process.</summary>
    /// <param name="ct">The startup cancellation token.</param>
    /// <returns>A task representing process startup.</returns>
    Task StartAsync(CancellationToken ct);

    /// <summary>Asynchronously sends a command and drains a terminal response when cancellation closes stdin.</summary>
    /// <param name="request">The command envelope.</param>
    /// <param name="ct">The cancellation token used to disconnect and begin bounded cleanup.</param>
    /// <returns>A task containing the response, including a drained terminal response after cancellation.</returns>
    Task<AutomationResponseEnvelope> SendAsync(AutomationRequestEnvelope request, CancellationToken ct);
}
