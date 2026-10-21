namespace Codexplorer.Automation.AgentComparison;

/// <summary>Defines the shared runner clarification boundary.</summary>
internal interface IComparisonHelper
{
    /// <summary>Asynchronously answers a runtime clarification using the existing runner context.</summary>
    /// <param name="invocation">The isolated session.</param>
    /// <param name="question">The explicit runner question.</param>
    /// <param name="assistantText">The originating assistant text.</param>
    /// <param name="callsConsumed">The current main-call consumption.</param>
    /// <param name="wrapUpSent">Whether finalization has started.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task containing the helper answer.</returns>
    Task<string> AnswerAsync(RuntimeInvocation invocation, string question, string? assistantText, int callsConsumed,
        bool wrapUpSent, CancellationToken cancellationToken);
}
