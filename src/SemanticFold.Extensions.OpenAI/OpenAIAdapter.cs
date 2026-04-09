using OpenAI.Chat;
using SemanticFold.Core.Enums;
using SemanticFold.Core.Models;
using SemanticFold.Core.Models.Content;

namespace SemanticFold.Extensions.OpenAI;

/// <summary>
/// Extension methods for converting between SemanticFold abstractions and the OpenAI chat SDK.
/// </summary>
/// <remarks>
/// This class covers both directions of the adapter:
/// <list type="bullet">
///   <item>Outbound — <see cref="ForOpenAI"/> converts <see cref="Message"/> instances to OpenAI chat messages before sending.</item>
///   <item>Inbound — <see cref="ResponseBlocks"/>, <see cref="TextBlocks"/>, and <see cref="ToolUseBlocks"/> extract content
///   from a <see cref="ChatCompletion"/> to pass back into <c>ConversationContext.RecordModelResponse</c>.</item>
/// </list>
/// </remarks>
public static class OpenAIExtensions
{
    /// <summary>
    /// Converts SemanticFold messages into OpenAI chat messages, preserving order.
    /// Call this on the result of <c>ConversationContext.PrepareAsync()</c> immediately before sending to the OpenAI client.
    /// </summary>
    /// <param name="messages">The prepared SemanticFold messages.</param>
    /// <returns>A list of OpenAI <see cref="ChatMessage"/> instances ready to pass to <c>CompleteChatAsync</c>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="messages"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a message has an unrecognized role.</exception>
    public static IReadOnlyList<ChatMessage> ForOpenAI(this IReadOnlyList<Message> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        List<ChatMessage> result = new(messages.Count);

        foreach (Message message in messages)
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

                    foreach (ToolUseContent toolUse in message.Content.OfType<ToolUseContent>())
                    {
                        assistant.ToolCalls.Add(ChatToolCall.CreateFunctionToolCall(
                            toolUse.ToolCallId,
                            toolUse.ToolName,
                            BinaryData.FromString(toolUse.ArgumentsJson)));
                    }

                    result.Add(assistant);
                    break;

                case MessageRole.Tool:
                    ToolResultContent? toolResult = message.Content.OfType<ToolResultContent>().FirstOrDefault();

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
    /// Extracts all content blocks from a <see cref="ChatCompletion"/> — both text and tool call requests.
    /// This is the value to pass to <c>ConversationContext.RecordModelResponse</c> in the typical agent loop.
    /// </summary>
    /// <param name="response">The OpenAI chat completion response.</param>
    /// <returns>
    /// A list of <see cref="ContentBlock"/> instances. Contains <see cref="TextContent"/> for any non-empty
    /// text response, and <see cref="ToolUseContent"/> for each tool call the model requested.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="response"/> is null.</exception>
    public static IReadOnlyList<ContentBlock> ResponseBlocks(this ChatCompletion response) =>
        [.. response.TextBlocks(), .. response.ToolUseBlocks()];

    /// <summary>
    /// Extracts only the text content blocks from a <see cref="ChatCompletion"/>.
    /// Use this when you need just the model's text response without tool call metadata,
    /// for example to check task completion conditions or display output to the user.
    /// </summary>
    /// <param name="response">The OpenAI chat completion response.</param>
    /// <returns>
    /// A list of <see cref="TextContent"/> blocks. Empty if the response contained no non-whitespace text.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="response"/> is null.</exception>
    public static IReadOnlyList<TextContent> TextBlocks(this ChatCompletion response)
    {
        ArgumentNullException.ThrowIfNull(response);

        List<TextContent> blocks = [];

        foreach (ChatMessageContentPart part in response.Content)
        {
            if (!string.IsNullOrWhiteSpace(part.Text))
                blocks.Add(new TextContent(part.Text));
        }

        return blocks;
    }

    /// <summary>
    /// Extracts the tool call requests from a <see cref="ChatCompletion"/> as <see cref="ToolUseContent"/> blocks.
    /// Use this when you need the tool call blocks separately, for example to fan out dispatch logic
    /// before combining with text blocks for <c>RecordModelResponse</c>.
    /// </summary>
    /// <param name="response">The OpenAI chat completion response.</param>
    /// <returns>
    /// A list of <see cref="ToolUseContent"/> blocks, one per tool call requested by the model.
    /// Empty if the model made no tool calls.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="response"/> is null.</exception>
    public static IReadOnlyList<ToolUseContent> ToolUseBlocks(this ChatCompletion response)
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

    private static string ExtractText(Message message) =>
        message.Content.OfType<TextContent>().FirstOrDefault()?.Text ?? string.Empty;
}
