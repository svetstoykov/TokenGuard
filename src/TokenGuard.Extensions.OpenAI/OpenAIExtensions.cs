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
    /// Converts TokenGuard messages into OpenAI chat messages, preserving order.
    /// Call this on the result of <c>ConversationContext.PrepareAsync()</c> immediately before sending to the OpenAI client.
    /// </summary>
    /// <param name="messages">The prepared TokenGuard messages.</param>
    /// <returns>A list of OpenAI <see cref="ChatMessage"/> instances ready to pass to <c>CompleteChatAsync</c>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="messages"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a message has an unrecognized role.</exception>
    public static IReadOnlyList<ChatMessage> ForOpenAI(this IReadOnlyList<ContextMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        List<ChatMessage> result = new(messages.Count);

        foreach (var message in messages)
        {
            switch (message.Role)
            {
                case MessageRole.System:
                    result.Add(new SystemChatMessage(ExtractText(message)));
                    break;

                case MessageRole.User:
                    result.Add(new UserChatMessage(ExtractText(message)));
                    break;

                case MessageRole.Model:
                    AssistantChatMessage assistant = new(ExtractText(message));

                    foreach (var toolUse in message.Segments.OfType<ToolUseContent>())
                    {
                        assistant.ToolCalls.Add(ChatToolCall.CreateFunctionToolCall(
                            toolUse.ToolCallId,
                            toolUse.ToolName,
                            BinaryData.FromString(toolUse.Content)));
                    }

                    result.Add(assistant);
                    break;

                case MessageRole.Tool:
                    var toolResult = message.Segments.OfType<ToolResultContent>().FirstOrDefault();

                    if (toolResult is not null)
                        result.Add(new ToolChatMessage(toolResult.ToolCallId, toolResult.Content));

                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(message.Role), message.Role, "Unsupported message role.");
            }
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
    /// Empty if the model made no tool calls.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="response"/> is null.</exception>
    public static IReadOnlyList<ToolUseContent> ToolUseSegments(this ChatCompletion response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return response.ToolCalls
            .Select(call => new ToolUseContent(call.Id, call.FunctionName, call.FunctionArguments.ToString()))
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

    private static string ExtractText(ContextMessage contextMessage) =>
        contextMessage.Segments.OfType<TextContent>().FirstOrDefault()?.Content ?? string.Empty;
}
