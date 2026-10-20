using System.Text.Json;
using Microsoft.Extensions.AI;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;
using ToolResultContent = TokenGuard.Core.Models.Content.ToolResultContent;
using TextContent = TokenGuard.Core.Models.Content.TextContent;

namespace TokenGuard.Extensions.AI;

/// <summary>
///     Provides conversions between TokenGuard history and Microsoft.Extensions.AI messages and responses.
/// </summary>
/// <remarks>
///     The caller owns the conversation context and drives each tool round so every request and result can be recorded.
/// </remarks>
public static class ChatClientExtensions
{
    /// <summary>Converts prepared history into chat messages, preserving segment order.</summary>
    /// <param name="messages">The prepared history. Cannot be <see langword="null" />.</param>
    /// <returns>The chat messages to send to an <see cref="IChatClient" />.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="messages" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">
    ///     A segment cannot be represented by its role, tool arguments are not a JSON object, or tool calls and results are unpaired.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">A message has an unrecognized role.</exception>
    public static IReadOnlyList<ChatMessage> ForChatClient(this IReadOnlyList<ContextMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        List<ChatMessage> result = new(messages.Count);
        HashSet<string> pending = [];

        foreach (var message in messages)
        {
            var role = message.Role switch
            {
                MessageRole.System => ChatRole.System,
                MessageRole.User => ChatRole.User,
                MessageRole.Model => ChatRole.Assistant,
                MessageRole.Tool => ChatRole.Tool,
                _ => throw new ArgumentOutOfRangeException(nameof(messages), message.Role, "Unsupported message role."),
            };

            if (message.Role != MessageRole.Tool && pending.Count > 0)
            {
                throw new InvalidOperationException("Assistant tool calls must be followed by matching tool results before the next message.");
            }

            List<AIContent> contents = [];
            foreach (var segment in message.Segments)
            {
                switch (segment)
                {
                    case TextContent text when message.Role != MessageRole.Tool:
                        if (!string.IsNullOrWhiteSpace(text.Content))
                        {
                            contents.Add(new Microsoft.Extensions.AI.TextContent(text.Content));
                        }
                        break;
                    case ToolUseContent call when message.Role == MessageRole.Model:
                        contents.Add(new FunctionCallContent(call.ToolCallId, call.ToolName, ToolArguments(call)));
                        if (!pending.Add(call.ToolCallId))
                        {
                            throw new InvalidOperationException($"Tool call '{call.ToolCallId}' appears more than once in the same turn.");
                        }
                        break;
                    case ToolResultContent toolResult when message.Role == MessageRole.Tool:
                        if (!pending.Remove(toolResult.ToolCallId))
                        {
                            throw new InvalidOperationException($"Tool result '{toolResult.ToolCallId}' has no preceding assistant tool call.");
                        }
                        result.Add(new ChatMessage(role, [new FunctionResultContent(toolResult.ToolCallId, toolResult.Content)]));
                        break;
                    default:
                        throw new InvalidOperationException($"A {message.Role} message cannot carry a {segment.GetType().Name} segment.");
                }
            }

            if (message.Role == MessageRole.Tool)
            {
                continue;
            }

            if (contents.Count == 0)
            {
                throw new InvalidOperationException($"A {message.Role} message contains no text or tool calls that can be sent.");
            }
            result.Add(new ChatMessage(role, contents));
        }

        if (pending.Count > 0)
        {
            throw new InvalidOperationException("Assistant tool calls have no matching tool results at the end of the prepared history.");
        }
        return result;
    }

    /// <summary>Extracts text and tool calls from every response message in their original order.</summary>
    /// <param name="response">The response to record. Cannot be <see langword="null" />.</param>
    /// <returns>The surfaced segments; reasoning and usage content are omitted.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="response" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">
    ///     The response contains an automatically invoked function result, a failed function-call mapping, or an informational function call.
    /// </exception>
    /// <exception cref="NotSupportedException">The response contains unsupported content or unserializable function arguments.</exception>
    /// <exception cref="JsonException">Function arguments cannot be serialized as JSON.</exception>
    /// <exception cref="ArgumentException">A function call has a blank identifier or name.</exception>
    public static IReadOnlyList<ContentSegment> ResponseSegments(this ChatResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var contents = response.Messages.SelectMany(message => message.Contents).ToArray();
        if (contents.Any(content => content is FunctionResultContent))
        {
            throw new InvalidOperationException(
                "The response contains function results from automatic function invocation. Drive the tool loop in the caller " +
                "or remove automatic function invocation from the chat client pipeline.");
        }

        List<ContentSegment> segments = [];
        foreach (var content in contents)
        {
            switch (content)
            {
                case Microsoft.Extensions.AI.TextContent text:
                    if (!string.IsNullOrWhiteSpace(text.Text))
                    {
                        segments.Add(new TextContent(text.Text));
                    }
                    break;
                case FunctionCallContent call:
                    if (call.Exception is not null)
                    {
                        throw new InvalidOperationException($"Function call '{call.CallId}' mapping failed; its arguments cannot be executed.",
                            call.Exception);
                    }
                    if (call.InformationalOnly)
                    {
                        throw new InvalidOperationException(
                            $"Function call '{call.CallId}' is informational and cannot be represented as an executable tool request.");
                    }
                    var arguments = call.Arguments is null ? "{}" : JsonSerializer.Serialize(call.Arguments);
                    segments.Add(new ToolUseContent(call.CallId, call.Name, arguments));
                    break;
                case TextReasoningContent:
                case UsageContent:
                    break;
                default:
                    throw new NotSupportedException($"Response content type '{content.GetType().Name}' is not supported by TokenGuard.");
            }
        }
        return segments;
    }

    /// <summary>Extracts response text after validating all response content.</summary>
    /// <param name="response">The response to inspect. Cannot be <see langword="null" />.</param>
    /// <returns>The non-whitespace text segments in response order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="response" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">
    ///     The response contains an automatically invoked function result, a failed function-call mapping, or an informational function call.
    /// </exception>
    /// <exception cref="NotSupportedException">The response contains unsupported content or unserializable function arguments.</exception>
    /// <exception cref="JsonException">Function arguments cannot be serialized as JSON.</exception>
    /// <exception cref="ArgumentException">A function call has a blank identifier or name.</exception>
    public static IReadOnlyList<TextContent> TextSegments(this ChatResponse response) => [.. response.ResponseSegments().OfType<TextContent>()];

    /// <summary>Extracts response tool requests after validating all response content.</summary>
    /// <param name="response">The response to inspect. Cannot be <see langword="null" />.</param>
    /// <returns>The tool-use segments in response order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="response" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">
    ///     The response contains an automatically invoked function result, a failed function-call mapping, or an informational function call.
    /// </exception>
    /// <exception cref="NotSupportedException">The response contains unsupported content or unserializable function arguments.</exception>
    /// <exception cref="JsonException">Function arguments cannot be serialized as JSON.</exception>
    /// <exception cref="ArgumentException">A function call has a blank identifier or name.</exception>
    public static IReadOnlyList<ToolUseContent> ToolUseSegments(this ChatResponse response)
        => [.. response.ResponseSegments().OfType<ToolUseContent>()];

    /// <summary>Reads the provider input count for correcting the context's estimates.</summary>
    /// <param name="response">The response to inspect. Cannot be <see langword="null" />.</param>
    /// <returns>A positive input count capped at <see cref="int.MaxValue" />, or <see langword="null" /> when absent or nonpositive.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="response" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">The response contains an automatically invoked function result.</exception>
    public static int? InputTokens(this ChatResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (response.Messages.SelectMany(message => message.Contents).Any(content => content is FunctionResultContent))
        {
            throw new InvalidOperationException(
                "Automatic function invocation combines tool rounds. Drive the tool loop in the caller or remove automatic function invocation.");
        }
        return ToTokenCount(response.Usage?.InputTokenCount);
    }

    /// <summary>Maps provider token counts into the positive range used by TokenGuard.</summary>
    /// <param name="tokens">The reported count, or <see langword="null" /> when absent.</param>
    /// <returns>A positive bounded count, or <see langword="null" /> when absent or nonpositive.</returns>
    internal static int? ToTokenCount(long? tokens) => tokens is > 0 ? (int)Math.Min(tokens.Value, int.MaxValue) : null;

    // Object values exist only at the framework boundary; each value is a JsonElement detached from its source document.
    private static Dictionary<string, object?> ToolArguments(ToolUseContent call)
    {
        try
        {
            var arguments = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(call.Content);
            if (arguments is not null)
            {
                return arguments.ToDictionary(pair => pair.Key, pair => (object?)pair.Value);
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Arguments of tool call '{call.ToolCallId}' must be a JSON object.", exception);
        }
        throw new InvalidOperationException($"Arguments of tool call '{call.ToolCallId}' must be a JSON object.");
    }
}
