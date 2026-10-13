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
///   <item>Outbound — <see cref="ForAnthropic"/> converts prepared <see cref="ContextMessage"/> instances into Anthropic-compatible <see cref="MessageParam"/> values plus the separate system payload required by Anthropic. Request construction remains at the call site.</item>
///   <item>Inbound — <see cref="ResponseSegments"/>, <see cref="TextSegments"/>, and <see cref="ToolUseSegments"/> extract model text and tool requests from an Anthropic <see cref="Message"/> to pass back into <c>ConversationContext.RecordModelResponse</c>.</item>
/// </list>
/// </remarks>
public static class AnthropicExtensions
{
    /// <summary>
    /// Converts TokenGuard messages into Anthropic-compatible message content, preserving order and content.
    /// Call this on the result of <c>ConversationContext.PrepareAsync()</c> immediately before constructing a
    /// <see cref="MessageCreateParams"/> request.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every text segment that holds non-whitespace text becomes its own text block, in order. The message list has one entry per
    /// user, model and tool message: a model message carries its text blocks followed by one tool-use block per tool call, and a
    /// tool message becomes one user message with one tool-result block per result.
    /// </para>
    /// <para>
    /// System messages are kept out of the message list. Their text blocks form the separate system payload, in order.
    /// </para>
    /// </remarks>
    /// <param name="messages">The prepared TokenGuard messages.</param>
    /// <returns>
    /// A tuple containing the Anthropic <see cref="MessageParam"/> list and the system payload built from system messages,
    /// or <see langword="null"/> as the system payload when there is no system message.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="messages"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a message holds content its role cannot carry in an Anthropic request, such as a user message without text,
    /// a tool message without a tool result, or a tool call whose arguments are not a JSON object,
    /// -or-
    /// when the history contains tool calls without matching results or tool results without a preceding tool call.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a message has an unrecognized role.</exception>
    public static (List<MessageParam> Messages, MessageCreateParamsSystem? System) ForAnthropic(this IReadOnlyList<ContextMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        List<MessageParam> result = new(messages.Count);
        List<TextBlockParam> systemBlocks = [];
        HashSet<string> pendingToolCallIds = [];

        foreach (var message in messages)
        {
            switch (message.Role)
            {
                case MessageRole.System:
                    EnsureNoPendingToolCalls(message.Role, pendingToolCallIds);
                    systemBlocks.AddRange(TextBlocks(message, requireText: true));
                    break;

                case MessageRole.User:
                    EnsureNoPendingToolCalls(message.Role, pendingToolCallIds);
                    List<ContentBlockParam> userBlocks = [.. TextBlocks(message, requireText: true)];
                    result.Add(new MessageParam { Role = Role.User, Content = userBlocks });
                    break;

                case MessageRole.Model:
                    EnsureNoPendingToolCalls(message.Role, pendingToolCallIds);
                    List<ContentBlockParam> assistantBlocks = [.. TextBlocks(message, requireText: false)];

                    foreach (var toolUse in message.Segments.OfType<ToolUseContent>())
                    {
                        var input = ToolInput(message.Role, toolUse);
                        assistantBlocks.Add(new ToolUseBlockParam { ID = toolUse.ToolCallId, Name = toolUse.ToolName, Input = input });
                        pendingToolCallIds.Add(toolUse.ToolCallId);
                    }

                    if (assistantBlocks.Count == 0)
                    {
                        throw new InvalidOperationException(
                            $"A {message.Role} message cannot be sent to Anthropic because it contains neither text nor a tool call.");
                    }

                    result.Add(new MessageParam { Role = Role.Assistant, Content = assistantBlocks });
                    break;

                case MessageRole.Tool:
                    var toolResults = message.Segments.OfType<ToolResultContent>().ToList();

                    if (toolResults.Count == 0 || toolResults.Count != message.Segments.Count)
                    {
                        throw new InvalidOperationException(
                            $"A {message.Role} message cannot be sent to Anthropic because it must contain only tool results and at least one.");
                    }

                    List<ContentBlockParam> toolResultBlocks = new(toolResults.Count);

                    foreach (var toolResult in toolResults)
                    {
                        if (!pendingToolCallIds.Remove(toolResult.ToolCallId))
                        {
                            throw new InvalidOperationException(
                                $"Tool result '{toolResult.ToolCallId}' has no preceding assistant tool call in the prepared history.");
                        }

                        toolResultBlocks.Add(new ToolResultBlockParam { ToolUseID = toolResult.ToolCallId, Content = toolResult.Content });
                    }

                    result.Add(new MessageParam { Role = Role.User, Content = toolResultBlocks });
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

        return (result, systemBlocks.Count == 0 ? null : new MessageCreateParamsSystem(systemBlocks));
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
    /// Extracts the provider-reported total input token count of the request from an Anthropic response message.
    /// Pass this as the second argument to <c>ConversationContext.RecordModelResponse</c> to enable
    /// anchor-based token estimation correction.
    /// </summary>
    /// <remarks>
    /// The total is the sum of the uncached input tokens, the tokens written to the prompt cache and the tokens read from it,
    /// so it covers the whole request whether or not prompt caching served part of it.
    /// </remarks>
    /// <param name="response">The Anthropic response message.</param>
    /// <returns>The total input token count, or <see langword="null"/> if usage data was not included in the response.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="response"/> is null.</exception>
    public static int? InputTokens(this Message response)
    {
        ArgumentNullException.ThrowIfNull(response);

        // The SDK throws on reading Usage when the payload carries none, so presence is checked on the raw payload.
        if (!response.RawData.TryGetValue("usage", out var rawUsage) || rawUsage.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var usage = response.Usage;
        return (int)(usage.InputTokens + (usage.CacheCreationInputTokens ?? 0) + (usage.CacheReadInputTokens ?? 0));
    }

    private static List<TextBlockParam> TextBlocks(ContextMessage message, bool requireText)
    {
        List<TextBlockParam> blocks = [];

        foreach (var segment in message.Segments)
        {
            if (segment is TextContent text)
            {
                // Anthropic rejects a text block without non-whitespace text.
                if (!string.IsNullOrWhiteSpace(text.Content))
                {
                    blocks.Add(new TextBlockParam { Text = text.Content });
                }
            }
            else if (segment is not ToolUseContent || message.Role != MessageRole.Model)
            {
                throw new InvalidOperationException(
                    $"A {message.Role} message cannot be sent to Anthropic because it contains a {segment.GetType().Name} segment.");
            }
        }

        if (blocks.Count == 0 && requireText)
        {
            throw new InvalidOperationException($"A {message.Role} message cannot be sent to Anthropic because it contains no text.");
        }

        return blocks;
    }

    private static IReadOnlyDictionary<string, JsonElement> ToolInput(MessageRole role, ToolUseContent toolUse)
    {
        var problem =
            $"A {role} message cannot be sent to Anthropic because the arguments of tool call '{toolUse.ToolCallId}' are not a JSON object.";

        try
        {
            var input = JsonSerializer.Deserialize<IReadOnlyDictionary<string, JsonElement>>(toolUse.Content);
            return input ?? throw new InvalidOperationException(problem);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(problem, exception);
        }
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
