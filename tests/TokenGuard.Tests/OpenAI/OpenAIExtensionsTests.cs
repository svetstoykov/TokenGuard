using FluentAssertions;
using OpenAI.Chat;
using System.Text.Json;
using System.ClientModel.Primitives;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;
using TokenGuard.Extensions.OpenAI;

namespace TokenGuard.Tests.OpenAI;

public sealed class OpenAIExtensionsTests
{
    [Fact]
    public void ForOpenAI_WhenMessagesContainEachSupportedRole_ConvertsMessagesInOrder()
    {
        // Arrange
        IReadOnlyList<ContextMessage> messages =
        [
            ContextMessage.FromText(MessageRole.System, "system prompt"),
            ContextMessage.FromText(MessageRole.User, "user input"),
            new ContextMessage
            {
                Role = MessageRole.Model,
                Segments =
                [
                    new TextContent("assistant reply"),
                    new ToolUseContent("call_1", "search", "{\"query\":\"token guard\"}"),
                ],
            },
            new ContextMessage
            {
                Role = MessageRole.Tool,
                Segments =
                [
                    new ToolResultContent("call_1", "search", "search result"),
                ],
            },
        ];

        // Act
        var result = messages.ForOpenAI();

        // Assert
        result.Should().HaveCount(4);

        result[0].Should().BeOfType<SystemChatMessage>();
        result[0].Content[0].Text.Should().Be("system prompt");

        result[1].Should().BeOfType<UserChatMessage>();
        result[1].Content[0].Text.Should().Be("user input");

        var assistant = result[2].Should().BeOfType<AssistantChatMessage>().Subject;
        assistant.Content[0].Text.Should().Be("assistant reply");
        assistant.ToolCalls.Should().ContainSingle();
        assistant.ToolCalls[0].Id.Should().Be("call_1");
        assistant.ToolCalls[0].FunctionName.Should().Be("search");
        assistant.ToolCalls[0].FunctionArguments.ToString().Should().Be("{\"query\":\"token guard\"}");

        var tool = result[3].Should().BeOfType<ToolChatMessage>().Subject;
        tool.ToolCallId.Should().Be("call_1");
        tool.Content[0].Text.Should().Be("search result");
    }

    [Fact]
    public void ForOpenAI_WhenModelMessageContainsOnlyToolUse_UsesEmptyAssistantText()
    {
        // Arrange
        IReadOnlyList<ContextMessage> messages =
        [
            new ContextMessage
            {
                Role = MessageRole.Model,
                Segments =
                [
                    new ToolUseContent("call_1", "read_file", "{}"),
                ],
            },
        ];

        // Act
        var result = messages.ForOpenAI();

        // Assert
        result.Should().ContainSingle();
        var assistant = result[0].Should().BeOfType<AssistantChatMessage>().Subject;
        assistant.Content.Should().ContainSingle();
        assistant.Content[0].Text.Should().BeEmpty();
        assistant.ToolCalls.Should().ContainSingle();
    }

    [Fact]
    public void ForOpenAI_WhenToolMessageHasNoToolResult_SkipsMessage()
    {
        // Arrange
        IReadOnlyList<ContextMessage> messages =
        [
            new ContextMessage
            {
                Role = MessageRole.Tool,
                Segments =
                [
                    new TextContent("not a tool result"),
                ],
            },
        ];

        // Act
        var result = messages.ForOpenAI();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ForOpenAI_WhenToolMessageIsMaskedWithToolResultContent_EmitsToolChatMessage()
    {
        // Arrange
        IReadOnlyList<ContextMessage> messages =
        [
            new ContextMessage
            {
                Role = MessageRole.Model,
                Segments =
                [
                    new ToolUseContent("call_1", "search", "{\"query\":\"token guard\"}"),
                ],
            },
            new ContextMessage
            {
                Role = MessageRole.Tool,
                State = CompactionState.Masked,
                Segments =
                [
                    new ToolResultContent("call_1", "search", "[Tool result cleared - search, call_1]"),
                ],
            },
        ];

        // Act
        var result = messages.ForOpenAI();

        // Assert
        result.Should().HaveCount(2);

        var assistant = result[0].Should().BeOfType<AssistantChatMessage>().Subject;
        assistant.ToolCalls.Should().ContainSingle();
        assistant.ToolCalls[0].Id.Should().Be("call_1");

        var tool = result[1].Should().BeOfType<ToolChatMessage>().Subject;
        tool.ToolCallId.Should().Be("call_1");
        tool.Content[0].Text.Should().Be("[Tool result cleared - search, call_1]");
    }

    [Fact]
    public void ForOpenAI_WhenMaskedAndUnmaskedToolMessagesShareToolCalls_EmitsToolChatMessageForEachCall()
    {
        // Arrange
        IReadOnlyList<ContextMessage> messages =
        [
            new ContextMessage
            {
                Role = MessageRole.Model,
                Segments =
                [
                    new ToolUseContent("call_masked", "search", "{\"query\":\"token guard\"}"),
                    new ToolUseContent("call_unmasked", "fetch", "{\"id\":2}"),
                ],
            },
            new ContextMessage
            {
                Role = MessageRole.Tool,
                State = CompactionState.Masked,
                Segments =
                [
                    new ToolResultContent("call_masked", "search", "[Tool result cleared - search, call_masked]"),
                ],
            },
            new ContextMessage
            {
                Role = MessageRole.Tool,
                Segments =
                [
                    new ToolResultContent("call_unmasked", "fetch", "{\"value\":42}"),
                ],
            },
        ];

        // Act
        var result = messages.ForOpenAI();

        // Assert
        result.Should().HaveCount(3);

        var assistant = result[0].Should().BeOfType<AssistantChatMessage>().Subject;
        assistant.ToolCalls.Should().HaveCount(2);
        assistant.ToolCalls.Select(call => call.Id).Should().Equal("call_masked", "call_unmasked");

        var toolMessages = result.Skip(1).Should().AllBeOfType<ToolChatMessage>().Subject.Cast<ToolChatMessage>().ToList();
        toolMessages.Should().HaveCount(2);
        toolMessages.Select(message => message.ToolCallId).Should().Equal("call_masked", "call_unmasked");
    }

    [Fact]
    public void ForOpenAI_WhenConversationContainsMaskedToolResult_DoesNotLeaveOrphanedToolCalls()
    {
        // Arrange
        IReadOnlyList<ContextMessage> messages =
        [
            ContextMessage.FromText(MessageRole.System, "system prompt"),
            ContextMessage.FromText(MessageRole.User, "user input"),
            new ContextMessage
            {
                Role = MessageRole.Model,
                Segments =
                [
                    new ToolUseContent("call_masked", "search", "{\"query\":\"token guard\"}"),
                ],
            },
            new ContextMessage
            {
                Role = MessageRole.Tool,
                State = CompactionState.Masked,
                Segments =
                [
                    new ToolResultContent("call_masked", "search", "[Tool result cleared - search, call_masked]"),
                ],
            },
            new ContextMessage
            {
                Role = MessageRole.Model,
                Segments =
                [
                    new ToolUseContent("call_unmasked", "fetch", "{\"id\":2}"),
                ],
            },
            new ContextMessage
            {
                Role = MessageRole.Tool,
                Segments =
                [
                    new ToolResultContent("call_unmasked", "fetch", "{\"value\":42}"),
                ],
            },
        ];

        // Act
        var result = messages.ForOpenAI();

        // Assert
        result.Should().HaveCount(6);
        result[0].Should().BeOfType<SystemChatMessage>();
        result[1].Should().BeOfType<UserChatMessage>();
        result[2].Should().BeOfType<AssistantChatMessage>();
        result[3].Should().BeOfType<ToolChatMessage>();
        result[4].Should().BeOfType<AssistantChatMessage>();
        result[5].Should().BeOfType<ToolChatMessage>();

        var toolMessages = result.OfType<ToolChatMessage>().ToList();
        toolMessages.Should().HaveCount(2);
        toolMessages.Select(message => message.ToolCallId).Should().Equal("call_masked", "call_unmasked");

        var assistantToolCallIds = result
            .OfType<AssistantChatMessage>()
            .SelectMany(message => message.ToolCalls)
            .Select(call => call.Id)
            .ToList();

        assistantToolCallIds.Should().Equal("call_masked", "call_unmasked");
        toolMessages.Select(message => message.ToolCallId).Should().Equal(assistantToolCallIds);
    }

    [Fact]
    public void ForOpenAI_WhenMessagesIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        IReadOnlyList<ContextMessage> messages = null!;

        // Act
        Action act = () => messages.ForOpenAI();

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ResponseSegments_WhenResponseContainsTextAndToolCalls_ReturnsCombinedSegments()
    {
        // Arrange
        var response = CreateChatCompletion(
            textParts: ["first", "second"],
            toolCalls:
            [
                ChatToolCall.CreateFunctionToolCall("call_1", "search", BinaryData.FromString("{\"q\":\"one\"}")),
                ChatToolCall.CreateFunctionToolCall("call_2", "fetch", BinaryData.FromString("{\"id\":2}")),
            ]);

        // Act
        var result = response.ResponseSegments();

        // Assert
        result.Should().HaveCount(4);
        result[0].Should().BeEquivalentTo(new TextContent("first"));
        result[1].Should().BeEquivalentTo(new TextContent("second"));
        result[2].Should().BeEquivalentTo(new ToolUseContent("call_1", "search", "{\"q\":\"one\"}"));
        result[3].Should().BeEquivalentTo(new ToolUseContent("call_2", "fetch", "{\"id\":2}"));
    }

    [Fact]
    public void TextSegments_WhenResponseContainsWhitespaceOnlyParts_FiltersThemOut()
    {
        // Arrange
        var response = CreateChatCompletion(textParts: ["useful", " ", "\t", string.Empty, "done"]);

        // Act
        var result = response.TextSegments();

        // Assert
        result.Should().BeEquivalentTo([new TextContent("useful"), new TextContent("done")]);
    }

    [Fact]
    public void ToolUseSegments_WhenResponseContainsToolCalls_MapsEachToolCall()
    {
        // Arrange
        var response = CreateChatCompletion(
            toolCalls:
            [
                ChatToolCall.CreateFunctionToolCall("call_1", "search", BinaryData.FromString("{\"q\":\"one\"}")),
                ChatToolCall.CreateFunctionToolCall("call_2", "fetch", BinaryData.FromString("{\"id\":2}")),
            ]);

        // Act
        var result = response.ToolUseSegments();

        // Assert
        result.Should().BeEquivalentTo(
        [
            new ToolUseContent("call_1", "search", "{\"q\":\"one\"}"),
            new ToolUseContent("call_2", "fetch", "{\"id\":2}"),
        ]);
    }

    [Fact]
    public void InputTokens_WhenUsageIsPresent_ReturnsInputTokenCount()
    {
        // Arrange
        var response = CreateChatCompletion(inputTokenCount: 42);

        // Act
        var result = response.InputTokens();

        // Assert
        result.Should().Be(42);
    }

    [Fact]
    public void InputTokens_WhenUsageIsMissing_ReturnsNull()
    {
        // Arrange
        var response = CreateChatCompletion();

        // Act
        var result = response.InputTokens();

        // Assert
        result.Should().BeNull();
    }

    /// <summary>
    /// Creates a chat completion payload for extension method tests.
    /// </summary>
    /// <param name="textParts">The text parts to include in the assistant message.</param>
    /// <param name="toolCalls">The tool calls to attach to the assistant message.</param>
    /// <param name="inputTokenCount">The optional prompt token count to include in usage metadata.</param>
    /// <returns>A chat completion instance deserialized from a synthetic payload.</returns>
    private static ChatCompletion CreateChatCompletion(
        IReadOnlyList<string>? textParts = null,
        IReadOnlyList<ChatToolCall>? toolCalls = null,
        int? inputTokenCount = null)
    {
        var message = new Dictionary<string, object?>
        {
            ["role"] = "assistant",
            ["content"] = (textParts ?? [])
                .Select(text => new Dictionary<string, object?>
                {
                    ["type"] = "text",
                    ["text"] = text,
                })
                .ToArray(),
        };

        if (toolCalls is not null && toolCalls.Count > 0)
        {
            message["tool_calls"] = toolCalls
                .Select(toolCall => new Dictionary<string, object?>
                {
                    ["id"] = toolCall.Id,
                    ["type"] = "function",
                    ["function"] = new Dictionary<string, object?>
                    {
                        ["name"] = toolCall.FunctionName,
                        ["arguments"] = toolCall.FunctionArguments.ToString(),
                    },
                })
                .ToArray();
        }

        var payload = new Dictionary<string, object?>
        {
            ["id"] = "chatcmpl_test",
            ["object"] = "chat.completion",
            ["created"] = 1700000000,
            ["model"] = "gpt-test",
            ["choices"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["index"] = 0,
                    ["finish_reason"] = "stop",
                    ["message"] = message,
                },
            },
        };

        if (inputTokenCount is int tokens)
        {
            payload["usage"] = new Dictionary<string, object?>
            {
                ["prompt_tokens"] = tokens,
                ["completion_tokens"] = 3,
                ["total_tokens"] = tokens + 3,
            };
        }

        return ModelReaderWriter.Read<ChatCompletion>(BinaryData.FromString(JsonSerializer.Serialize(payload)))!;
    }
}
