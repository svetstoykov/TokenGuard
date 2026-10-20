using TokenGuard.Core.Configuration;
using TokenGuard.Core.Summarization;

namespace TokenGuard.Core.Abstractions;

/// <summary>
///     Defines the model call that writes a summary of older conversation history.
/// </summary>
/// <remarks>
///     <para>
///         TokenGuard builds the request and checks the answer; an implementation only sends the request to a model. Send
///         <see cref="SummarizerRequest.SystemPrompt" /> as the system or instructions message and
///         <see cref="SummarizerRequest.UserPrompt" /> as one user message, cap the output at
///         <see cref="SummarizerRequest.MaxOutputTokens" />, and return what the model wrote with the reason it stopped and
///         the token usage it reported.
///     </para>
///     <para>
///         TokenGuard rejects a blank answer and a <see cref="SummarizerFinishReason.Refused" /> answer, accepts a
///         <see cref="SummarizerFinishReason.OutputLimit" /> answer and logs that it was cut, and logs every call. An
///         exception thrown by the implementation counts as a failed summary.
///     </para>
///     <para>
///         Register an implementation with <see cref="ConversationConfigBuilder.UseLlmSummarization" />. One instance serves
///         every context the configuration creates, so the implementation must be safe for concurrent use.
///     </para>
/// </remarks>
public interface ILlmSummarizer
{
    /// <summary>
    ///     Asynchronously sends one summarization request to a model.
    /// </summary>
    /// <param name="request">The prompts and the output cap to send. Cannot be <see langword="null" />.</param>
    /// <param name="cancellationToken">
    ///     A <see cref="CancellationToken" /> to observe while waiting for the task to complete.
    /// </param>
    /// <returns>
    ///     A task that represents the asynchronous operation. The task result contains the text the model wrote, the reason it
    ///     stopped, and the token usage it reported.
    /// </returns>
    Task<SummarizerResponse> SummarizeAsync(SummarizerRequest request, CancellationToken cancellationToken = default);
}
