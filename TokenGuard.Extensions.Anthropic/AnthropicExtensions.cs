using System.Text.Json;
using Anthropic.Models.Messages;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;

namespace TokenGuard.Extensions.Anthropic;

/// <summary>
/// Extension methods for converting between TokenGuard abstractions and the Anthropic messages SDK.
/// </summary>
/// <remarks>
/// This class covers both directions of the adapter:
/// <list type="bullet">
///   <item>Outbound — <see cref="ForAnthropic"/> converts prepared <see cref="ContextMessage"/> instances into Anthropic <see cref="MessageCreateParams"/> values, including the separate system field required by Anthropic.</item>
///   <item>Inbound — <see cref="ResponseSegments"/>, <see cref="TextSegments"/>, and <see cref="ToolUseSegments"/> extract model text and tool requests from an Anthropic <see cref="Anthropic.Models.Messages.Message"/> to pass back into <c>ConversationContext.RecordModelResponse</c>.</item>
/// </list>
/// </remarks>
public static class AnthropicExtensions
{
    /// <summary>
    /// Converts TokenGuard messages into an Anthropic request payload, preserving order.
    /// Call this on the result of <c>ConversationContext.PrepareAsync()</c> immediately before sending to <c>client.Messages.Create</c>.
    /// </summary>
    /// <param name="messages">The prepared TokenGuard messages.</param>
    /// <param name="model">The Anthropic model identifier.</param>
    /// <param name="maxTokens">The maximum number of tokens Anthropic may generate for the response.</param>
    /// <returns>A populated Anthropic <see cref="MessageCreateParams"/> request.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="messages"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="model"/> is null or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxTokens"/> is less than or equal to zero.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a message has an unrecognized role.</exception>
    public static MessageCreateParams ForAnthropic(this IReadOnlyList<ContextMessage> messages, string model, long maxTokens)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model cannot be null or whitespace.", nameof(model));
        }

        if (maxTokens <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTokens), maxTokens, "Max tokens must be greater than zero.");
        }

        List<MessageParam> result = new(messages.Count);

        foreach (var message in messages)
        {
            switch (message.Role)
            {
                case MessageRole.System:
                    break;

                case MessageRole.User:
                    result.Add(new MessageParam
                    {
                        Role = Role.User,
                        Content = new MessageParamContent(
                            new List<ContentBlockParam>
                            {
                                new TextBlockParam
                                {
                                    Text = ExtractText(message),
                                },
                            }),
                    });
                    break;

                case MessageRole.Model:
                    List<ContentBlockParam> assistantBlocks = [];
                    var text = ExtractText(message);

                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        assistantBlocks.Add(new TextBlockParam
                        {
                            Text = text,
                        });
                    }

                    foreach (var toolUse in message.Content.OfType<ToolUseContent>())
                    {
                        var input = JsonSerializer.Deserialize<IReadOnlyDictionary<string, JsonElement>>(toolUse.Content);

                        assistantBlocks.Add(new ToolUseBlockParam
                        {
                            ID = toolUse.ToolCallId,
                            Name = toolUse.ToolName,
                            Input = input ?? throw new JsonException("Tool arguments must deserialize to a JSON object."),
                        });
                    }

                    result.Add(new MessageParam
                    {
                        Role = Role.Assistant,
                        Content = assistantBlocks,
                    });
                    break;

                case MessageRole.Tool:
                    var toolResult = message.Content.OfType<ToolResultContent>().FirstOrDefault();

                    if (toolResult is not null)
                    {
                        result.Add(new MessageParam
                        {
                            Role = Role.User,
                            Content = new MessageParamContent(
                                new List<ContentBlockParam>
                                {
                                    new ToolResultBlockParam
                                    {
                                        ToolUseID = toolResult.ToolCallId,
                                        Content = toolResult.Content,
                                    },
                                }),
                        });
                    }

                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(message.Role), message.Role, "Unsupported message role.");
            }
        }

        return new MessageCreateParams
        {
            MaxTokens = maxTokens,
            Model = model,
            Messages = result,
            System = BuildSystemPrompt(messages),
        };
    }

    /// <summary>
    /// Extracts all surfaced content segments from an Anthropic response message — both text and tool call requests.
    /// This is the value to pass to <c>ConversationContext.RecordModelResponse</c> in the typical agent loop.
    /// </summary>
    /// <param name="response">The Anthropic response message.</param>
    /// <returns>
    /// A list of <see cref="ContentSegment"/> instances. Contains <see cref="TextContent"/> for any non-empty
    /// text response, and <see cref="ToolUseContent"/> for each tool call the model requested.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="response"/> is null.</exception>
    public static IReadOnlyList<ContentSegment> ResponseSegments(this Message response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return [.. response.TextSegments(), .. response.ToolUseSegments()];
    }

    /// <summary>
    /// Extracts only the text content segments from an Anthropic response message.
    /// Use this when you need just the model's text response without tool call metadata,
    /// for example to check task completion conditions or display output to the user.
    /// </summary>
    /// <param name="response">The Anthropic response message.</param>
    /// <returns>
    /// A list of <see cref="TextContent"/> segments. Empty if the response contained no non-whitespace text.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="response"/> is null.</exception>
    public static IReadOnlyList<TextContent> TextSegments(this Message response)
    {
        ArgumentNullException.ThrowIfNull(response);

        List<TextContent> segments = [];

        foreach (var block in response.Content)
        {
            if (block.TryPickText(out var text) && !string.IsNullOrWhiteSpace(text?.Text))
            {
                segments.Add(new TextContent(text.Text));
            }
        }

        return segments;
    }

    /// <summary>
    /// Extracts the tool call requests from an Anthropic response message as <see cref="ToolUseContent"/> segments.
    /// Use this when you need the tool call segments separately, for example to fan out dispatch logic
    /// before combining with text segments for <c>RecordModelResponse</c>.
    /// </summary>
    /// <param name="response">The Anthropic response message.</param>
    /// <returns>
    /// A list of <see cref="ToolUseContent"/> segments, one per tool call requested by the model.
    /// Empty if the model made no tool calls.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="response"/> is null.</exception>
    public static IReadOnlyList<ToolUseContent> ToolUseSegments(this Message response)
    {
        ArgumentNullException.ThrowIfNull(response);

        List<ToolUseContent> segments = [];

        foreach (var block in response.Content)
        {
            if (block.TryPickToolUse(out var toolUse) && toolUse is not null)
            {
                segments.Add(new ToolUseContent(toolUse.ID, toolUse.Name, JsonSerializer.Serialize(toolUse.Input)));
            }
        }

        return segments;
    }

    /// <summary>
    /// Extracts the provider-reported input token count from an Anthropic response message.
    /// Pass this as the second argument to <c>ConversationContext.RecordModelResponse</c> to enable
    /// anchor-based token estimation correction.
    /// </summary>
    /// <param name="response">The Anthropic response message.</param>
    /// <returns>The input token count, or <see langword="null"/> if usage data was not included in the response.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="response"/> is null.</exception>
    public static int? InputTokens(this Message response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return (int?)response.Usage?.InputTokens;
    }

    private static string ExtractText(ContextMessage message) =>
        message.Content.OfType<TextContent>().FirstOrDefault()?.Content ?? string.Empty;

    private static MessageCreateParamsSystem? BuildSystemPrompt(IReadOnlyList<ContextMessage> messages)
    {
        List<TextBlockParam> systemBlocks = [];

        foreach (var message in messages.Where(message => message.Role == MessageRole.System))
        {
            var text = ExtractText(message);
            if (!string.IsNullOrWhiteSpace(text))
            {
                systemBlocks.Add(new TextBlockParam
                {
                    Text = text,
                });
            }
        }

        return systemBlocks.Count == 0 ? null : new MessageCreateParamsSystem(systemBlocks);
    }
}
