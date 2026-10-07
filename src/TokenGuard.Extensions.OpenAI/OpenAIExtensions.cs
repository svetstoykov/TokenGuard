using OpenAI.Chat;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;

namespace TokenGuard.Extensions.OpenAI;

/// <summary>
/// Extension methods for converting between TokenGuard abstractions and the OpenAI chat SDK.
/// </summary>
/// <remarks>
/// This class covers both directions of the adapter:
/// <list type="bullet">
///   <item>Outbound — <see cref="ForOpenAI"/> converts <see cref="ContextMessage"/> instances to OpenAI chat messages before sending.</item>
///   <item>Inbound — <see cref="ResponseSegments"/>, <see cref="TextSegments"/>, and <see cref="ToolUseSegments"/> extract content
///   from a <see cref="ChatCompletion"/> to pass back into <c>ConversationContext.RecordModelResponse</c>.</item>
/// </list>
/// </remarks>
public static class OpenAIExtensions
{
    /// <summary>
    /// Converts TokenGuard messages into OpenAI chat messages, preserving order and content.
    /// Call this on the result of <c>ConversationContext.PrepareAsync()</c> immediately before sending to the OpenAI client.
    /// </summary>
    /// <remarks>
    /// Every text segment becomes its own content part, in order. The result has one message per input message, except
    /// that a <see cref="MessageRole.Tool"/> message holding several tool results becomes one OpenAI tool message per result.
    /// </remarks>
    /// <param name="messages">The prepared TokenGuard messages.</param>
    /// <returns>A list of OpenAI <see cref="ChatMessage"/> instances ready to pass to <c>CompleteChatAsync</c>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="messages"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a message holds content its role cannot carry in an OpenAI request, such as a tool message without a tool
    /// result, or when the history contains tool calls without matching results or tool results without a preceding tool call.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a message has an unrecognized role.</exception>
    public static IReadOnlyList<ChatMessage> ForOpenAI(this IReadOnlyList<ContextMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        List<ChatMessage> result = new(messages.Count);
        HashSet<string> pendingToolCallIds = [];

        foreach (var message in messages)
        {
            switch (message.Role)
            {
                case MessageRole.System:
                    EnsureNoPendingToolCalls(message.Role, pendingToolCallIds);
                    result.Add(new SystemChatMessage(TextParts(message, requireText: true)));
                    break;

                case MessageRole.User:
                    EnsureNoPendingToolCalls(message.Role, pendingToolCallIds);
                    result.Add(new UserChatMessage(TextParts(message, requireText: true)));
                    break;

                case MessageRole.Model:
                    EnsureNoPendingToolCalls(message.Role, pendingToolCallIds);
                    AssistantChatMessage assistant = new(TextParts(message, requireText: false));

                    foreach (var toolUse in message.Segments.OfType<ToolUseContent>())
                    {
                        assistant.ToolCalls.Add(ChatToolCall.CreateFunctionToolCall(
                            toolUse.ToolCallId,
                            toolUse.ToolName,
                            BinaryData.FromString(toolUse.Content)));
                    }

                    pendingToolCallIds = assistant.ToolCalls
                        .Select(static toolCall => toolCall.Id)
                        .ToHashSet(StringComparer.Ordinal);

                    result.Add(assistant);
                    break;

                case MessageRole.Tool:
                    var toolResults = message.Segments.OfType<ToolResultContent>().ToList();

                    if (toolResults.Count == 0 || toolResults.Count != message.Segments.Count)
                    {
                        throw new InvalidOperationException(
                            $"A {message.Role} message cannot be sent to OpenAI because it must contain only tool results and at least one.");
                    }

                    foreach (var toolResult in toolResults)
                    {
                        if (!pendingToolCallIds.Remove(toolResult.ToolCallId))
                        {
                            throw new InvalidOperationException(
                                $"Tool result '{toolResult.ToolCallId}' has no preceding assistant tool call in the prepared history.");
                        }

                        result.Add(new ToolChatMessage(toolResult.ToolCallId, toolResult.Content));
                    }

                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(message.Role), message.Role, "Unsupported message role.");
            }
        }

        if (pendingToolCallIds.Count > 0)
        {
            throw new InvalidOperationException(
                $"Assistant tool calls {string.Join(", ", pendingToolCallIds.Order(StringComparer.Ordinal).Select(id => $"'{id}'"))} " +
                "have no matching tool results at the end of the prepared history.");
        }

        return result;
    }

    /// <summary>
    /// Extracts all content segments from a <see cref="ChatCompletion"/> — both text and tool call requests.
    /// This is the value to pass to <c>ConversationContext.RecordModelResponse</c> in the typical agent loop.
    /// </summary>
    /// <param name="response">The OpenAI chat completion response.</param>
    /// <returns>
    /// A list of <see cref="ContentSegment"/> instances. Contains <see cref="TextContent"/> for any non-empty
    /// text response, and <see cref="ToolUseContent"/> for each tool call the model requested.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="response"/> is null.</exception>
    public static IReadOnlyList<ContentSegment> ResponseSegments(this ChatCompletion response) =>
        [.. response.TextSegments(), .. response.ToolUseSegments()];

    /// <summary>
    /// Extracts only the text content segments from a <see cref="ChatCompletion"/>.
    /// Use this when you need just the model's text response without tool call metadata,
    /// for example to check task completion conditions or display output to the user.
    /// </summary>
    /// <param name="response">The OpenAI chat completion response.</param>
    /// <returns>
    /// A list of <see cref="TextContent"/> segments. Empty if the response contained no non-whitespace text.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="response"/> is null.</exception>
    public static IReadOnlyList<TextContent> TextSegments(this ChatCompletion response)
    {
        ArgumentNullException.ThrowIfNull(response);

        List<TextContent> segments = [];

        foreach (var part in response.Content)
        {
            if (!string.IsNullOrWhiteSpace(part.Text))
                segments.Add(new TextContent(part.Text));
        }

        return segments;
    }

    /// <summary>
    /// Extracts the tool call requests from a <see cref="ChatCompletion"/> as <see cref="ToolUseContent"/> segments.
    /// Use this when you need the tool call segments separately, for example to fan out dispatch logic
    /// before combining with text segments for <c>RecordModelResponse</c>.
    /// </summary>
    /// <param name="response">The OpenAI chat completion response.</param>
    /// <returns>
    /// A list of <see cref="ToolUseContent"/> segments, one per tool call requested by the model.
    /// A tool call with empty or whitespace arguments gets the arguments <c>{}</c>. Empty if the model made no tool calls.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="response"/> is null.</exception>
    public static IReadOnlyList<ToolUseContent> ToolUseSegments(this ChatCompletion response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return response.ToolCalls
            .Select(call => new ToolUseContent(call.Id, call.FunctionName, NormalizeArguments(call.FunctionArguments.ToString())))
            .ToList();
    }

    /// <summary>
    /// Extracts the provider-reported input token count from a <see cref="ChatCompletion"/>.
    /// Pass this as the second argument to <c>ConversationContext.RecordModelResponse</c> to enable
    /// anchor-based token estimation correction.
    /// </summary>
    /// <param name="response">The OpenAI chat completion response.</param>
    /// <returns>The input token count, or <see langword="null"/> if usage data was not included in the response.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="response"/> is null.</exception>
    public static int? InputTokens(this ChatCompletion response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return response.Usage?.InputTokenCount;
    }

    private static string NormalizeArguments(string arguments) => string.IsNullOrWhiteSpace(arguments) ? "{}" : arguments;

    private static List<ChatMessageContentPart> TextParts(ContextMessage message, bool requireText)
    {
        List<ChatMessageContentPart> parts = [];

        foreach (var segment in message.Segments)
        {
            if (segment is TextContent text)
            {
                parts.Add(ChatMessageContentPart.CreateTextPart(text.Content));
            }
            else if (segment is not ToolUseContent || message.Role != MessageRole.Model)
            {
                throw new InvalidOperationException(
                    $"A {message.Role} message cannot be sent to OpenAI because it contains a {segment.GetType().Name} segment.");
            }
        }

        if (parts.Count == 0)
        {
            if (requireText)
            {
                throw new InvalidOperationException($"A {message.Role} message cannot be sent to OpenAI because it contains no text.");
            }

            parts.Add(ChatMessageContentPart.CreateTextPart(string.Empty));
        }

        return parts;
    }

    private static void EnsureNoPendingToolCalls(MessageRole nextRole, IReadOnlyCollection<string> pendingToolCallIds)
    {
        if (pendingToolCallIds.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Assistant tool calls must be followed by matching tool results before the next {nextRole} message.");
    }
}
