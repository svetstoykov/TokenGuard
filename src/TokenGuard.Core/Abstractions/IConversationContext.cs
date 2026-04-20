using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;

namespace TokenGuard.Core.Abstractions;

/// <summary>
/// Represents the state of one LLM conversation, tracking the full message history and preparing
/// the next request payload on demand.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IConversationContext"/> is the primary abstraction callers use to advance a conversation
/// without manually managing token limits, system prompts, tool results, or compaction behaviour.
/// The concrete implementation is <see cref="ConversationContext"/>, and instances
/// should be obtained from <see cref="IConversationContextFactory"/> rather than constructed directly
/// when dependency injection is in use.
/// </para>
/// <para>
/// Messages are recorded as they occur — user input via <see cref="AddUserMessage"/>, model replies
/// via <see cref="RecordModelResponse"/>, and tool outputs via <see cref="RecordToolResult"/>. The
/// underlying history is kept intact across all recording operations. Compaction is evaluated lazily
/// and only when <see cref="PrepareAsync"/> is called.
/// </para>
/// <para>
/// A context is scoped to a single conversation. Dispose it when the conversation ends to release the
/// history buffer. Implementations must throw <see cref="ObjectDisposedException"/> from all members
/// after disposal.
/// </para>
/// </remarks>
public interface IConversationContext : IDisposable
{
    /// <summary>
    /// Gets the full conversation history exactly as it has been recorded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a live, read-only view of the internal history. New messages recorded through this
    /// instance appear here immediately.
    /// </para>
    /// <para>
    /// This property is useful for inspection, logging, and debugging. It is not the same as the
    /// request payload returned by <see cref="PrepareAsync"/>, which may reflect a compacted subset.
    /// </para>
    /// </remarks>
    IReadOnlyList<ContextMessage> History { get; }

    /// <summary>
    /// Sets the system message for the conversation.
    /// </summary>
    /// <param name="text">The system prompt text.</param>
    /// <remarks>
    /// <para>
    /// A context stores at most one system message. If one already exists, this method replaces it
    /// in place rather than appending a second entry.
    /// </para>
    /// <para>
    /// The system message is kept at the front of the history and is preserved separately during
    /// compaction so that every prepared request continues to carry the active instructions.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="text"/> is null or whitespace.</exception>
    void SetSystemPrompt(string text);

    /// <summary>
    /// Appends a pinned message to the conversation history.
    /// </summary>
    /// <param name="role">The participant role that produced the message.</param>
    /// <param name="text">The plain-text payload to record.</param>
    /// <remarks>
    /// Pinned messages remain at their recorded position and are excluded from compaction and emergency truncation.
    /// Use this for durable instructions, constraints, or definitions that must always survive in the prepared payload.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="text"/> is null or whitespace.</exception>
    void AddPinnedMessage(MessageRole role, string text);

    /// <summary>
    /// Appends a pinned multi-segment message to the conversation history.
    /// </summary>
    /// <param name="role">The participant role that produced the message.</param>
    /// <param name="content">The ordered content segments that make up the pinned message payload.</param>
    /// <remarks>
    /// This overload preserves richer content layouts such as mixed text and tool-use segments while still ensuring the
    /// recorded message cannot be masked or dropped by later preparation steps.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="content"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="content"/> contains no segments.</exception>
    void AddPinnedMessage(MessageRole role, IEnumerable<ContentSegment> content);

    /// <summary>
    /// Appends a user message to the conversation history.
    /// </summary>
    /// <param name="text">The user message text.</param>
    /// <remarks>
    /// This method records the message as a new user turn and does not trigger compaction.
    /// Compaction is evaluated only when <see cref="PrepareAsync"/> is called.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="text"/> is null or whitespace.</exception>
    void AddUserMessage(string text);

    /// <summary>
    /// Records the model response as the next message in the conversation history.
    /// </summary>
    /// <param name="content">
    /// The content segments returned by the model. This can contain plain text, tool-use requests,
    /// or any other supported segment types for a model turn.
    /// </param>
    /// <param name="providerInputTokens">
    /// The exact input token count reported by the provider for the request that produced this
    /// response. When supplied, this value is used to correct future token estimates so
    /// <see cref="PrepareAsync"/> can stay aligned with the provider's actual counting behaviour.
    /// </param>
    /// <remarks>
    /// <para>
    /// Call this after each model response is received. The response is stored as a single
    /// <see cref="ContextMessage"/> with role <see cref="Enums.MessageRole.Model"/>, even when it
    /// spans multiple content segments.
    /// </para>
    /// <para>
    /// When <paramref name="providerInputTokens"/> is provided, the context compares the provider's
    /// count with its most recent prepared estimate and stores the difference as an additive correction
    /// factor. That correction is applied to later <see cref="PrepareAsync"/> calls until the next
    /// compaction cycle resets it.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="content"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="content"/> contains no segments.</exception>
    void RecordModelResponse(IEnumerable<ContentSegment> content, int? providerInputTokens = null);

    /// <summary>
    /// Records the result of one tool execution.
    /// </summary>
    /// <param name="toolCallId">The tool call identifier this result corresponds to.</param>
    /// <param name="toolName">The name of the tool that produced this result.</param>
    /// <param name="content">The tool output payload.</param>
    /// <remarks>
    /// <para>
    /// Call this once for each tool invocation requested by the model. The result is stored as a
    /// single <see cref="ContextMessage"/> with role <see cref="Enums.MessageRole.Tool"/> so
    /// later requests preserve the full tool call chain.
    /// </para>
    /// <para>
    /// This method only records the result. It does not validate that <paramref name="toolCallId"/>
    /// corresponds to a previously recorded <see cref="ToolUseContent"/> segment.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="toolCallId"/> or <paramref name="toolName"/> is null or whitespace.
    /// </exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="content"/> is null.</exception>
    void RecordToolResult(string toolCallId, string toolName, string content);

    /// <summary>
    /// Builds the message list to send for the next LLM request.
    /// </summary>
    /// <param name="cancellationToken">
    /// A token that can cancel asynchronous compaction before the prepared list is produced.
    /// </param>
    /// <returns>
    /// A task that resolves to the full history when it fits within the configured budget, or to a
    /// compacted message list when compaction is required.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is the main read operation of the context. Await it immediately before every provider
    /// request. The list it returns is what should be sent to the model.
    /// </para>
    /// <para>
    /// If the estimated token total is below the compaction trigger, the current history is returned
    /// directly. If the trigger is reached, the configured compaction strategy is awaited to produce
    /// a smaller list. Pinned messages are separated from the compactable set and reassembled into the
    /// final result at their original positions regardless of compaction outcome.
    /// </para>
    /// <para>
    /// Calling this method does not modify <see cref="History"/>. It only determines what subset or
    /// representation of that history should be sent next.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<ContextMessage>> PrepareAsync(CancellationToken cancellationToken = default);
}
