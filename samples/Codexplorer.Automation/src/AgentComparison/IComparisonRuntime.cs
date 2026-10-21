namespace Codexplorer.Automation.AgentComparison;

/// <summary>Defines an isolated benchmark runtime adapter.</summary>
internal interface IComparisonRuntime
{
    /// <summary>Asynchronously runs one session using the supplied loopback recorder.</summary>
    /// <param name="invocation">The isolated process and task contract.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task containing the partial or complete runtime outcome.</returns>
    Task<RuntimeResult> RunAsync(RuntimeInvocation invocation, CancellationToken cancellationToken);
}
