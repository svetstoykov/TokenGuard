using OpenAI.Chat;
using SemanticFold.Core.Enums;
using SemanticFold.Core.Models;
using SemanticFold.Core.Models.Content;

namespace SemanticFold.Adapters.OpenAI;

/// <summary>
/// Converts between SemanticFold message abstractions and OpenAI chat SDK types.
/// </summary>
public static class OpenAIAdapter
{
    /// <summary>
    /// Converts SemanticFold messages into OpenAI chat messages in their original order.
    /// </summary>
    /// <param name="messages">The SemanticFold messages to convert.</param>
    /// <returns>A read-only list of OpenAI chat messages.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="messages"/> is null.</exception>
    public static IReadOnlyList<ChatMessage> ForOpenAI(this IReadOnlyList<Message> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        List<ChatMessage> openAiMessages = [];

        foreach (Message message in messages)
        {
            switch (message.Role)
            {
                case MessageRole.System:
                    openAiMessages.Add(new SystemChatMessage(ExtractFirstText(message)));
                    break;

                case MessageRole.User:
                    openAiMessages.Add(new UserChatMessage(ExtractFirstText(message)));
                    break;

                case MessageRole.Model:
                    AssistantChatMessage assistantMessage = new(ExtractFirstText(message));

                    foreach (ToolUseContent toolUse in message.Content.OfType<ToolUseContent>())
                    {
                        assistantMessage.ToolCalls.Add(ChatToolCall.CreateFunctionToolCall(
                            toolUse.ToolCallId,
                            toolUse.ToolName,
                            BinaryData.FromString(toolUse.ArgumentsJson)));
                    }

                    openAiMessages.Add(assistantMessage);
                    break;

                case MessageRole.Tool:
                    ToolResultContent? toolResult = message.Content.OfType<ToolResultContent>().FirstOrDefault();

                    if (toolResult is not null)
                    {
                        openAiMessages.Add(new ToolChatMessage(toolResult.ToolCallId, toolResult.Content));
                    }

                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(message.Role), message.Role, "Unsupported message role.");
            }
        }

        return openAiMessages;
    }

    /// <summary>
    /// Converts an OpenAI chat completion into SemanticFold adapter output blocks and usage data.
    /// </summary>
    /// <param name="response">The OpenAI chat completion response to convert.</param>
    /// <returns>An adapter result containing extracted content blocks and the provider-reported input token count.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="response"/> is null.</exception>
    public static AdapterResult FromResponse(ChatCompletion response)
    {
        ArgumentNullException.ThrowIfNull(response);

        List<ContentBlock> blocks = [];

        if (response.Content.Count > 0)
        {
            string? text = response.Content[0].Text;

            if (!string.IsNullOrWhiteSpace(text))
            {
                blocks.Add(new TextContent(text));
            }
        }

        foreach (ChatToolCall call in response.ToolCalls)
        {
            blocks.Add(new ToolUseContent(call.Id, call.FunctionName, call.FunctionArguments.ToString()));
        }

        int? inputTokens = response.Usage?.InputTokenCount;
        return new AdapterResult(blocks.ToArray(), inputTokens);
    }

    private static string ExtractFirstText(Message message)
    {
        return message.Content.OfType<TextContent>().FirstOrDefault()?.Text ?? string.Empty;
    }
}
