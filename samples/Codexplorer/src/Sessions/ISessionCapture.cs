using TokenGuard.Core.Models;

namespace Codexplorer.Sessions;

/// <summary>
///     Defines the passive record of every model call in one session.
/// </summary>
/// <remarks>
///     <para>
///         A capture only observes. It receives the messages already prepared for the provider and the response already
///         received, so the request, what the agent sees, and every token count are the same with or without it.
///     </para>
///     <para>
///         A capture belongs to one session and is disposed with it. Calls arrive one at a time.
///     </para>
/// </remarks>
internal interface ISessionCapture : IAsyncDisposable
{
    /// <summary>
    ///     Asynchronously records one model call and makes it durable before returning.
    /// </summary>
    /// <param name="modelCall">The model-call number, matching the turn numbering in the session transcript.</param>
    /// <param name="messages">The complete list of messages sent, which is the prepared view after compaction.</param>
    /// <param name="status">The call status: <c>completed</c>, <c>failed</c>, or <c>cancelled</c>.</param>
    /// <param name="response">The complete response, or <see langword="null" /> when the call did not return one.</param>
    /// <param name="ct">A <see cref="CancellationToken" /> to observe while waiting for the task to complete.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task WriteExchangeAsync(
        int modelCall, IReadOnlyList<ContextMessage> messages, string status, CapturedResponse? response, CancellationToken ct = default);
}
