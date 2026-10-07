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
            new ContextMessage { Role = MessageRole.Tool, Segments = [new ToolResultContent("call_1", "read_file", "ok")] },
        ];

        // Act
        var result = messages.ForOpenAI();

        // Assert
        result.Should().HaveCount(2);
        var assistant = result[0].Should().BeOfType<AssistantChatMessage>().Subject;
        assistant.Content.Should().ContainSingle();
        assistant.Content[0].Text.Should().BeEmpty();
        assistant.ToolCalls.Should().ContainSingle();
    }

    [Fact]
    public void ForOpenAI_WhenToolMessageHasNoToolResult_ThrowsInvalidOperationExceptionNamingRole()
    {
        // Arrange
        IReadOnlyList<ContextMessage> messages = [ContextMessage.FromText(MessageRole.Tool, "NOTE-THREE")];

        // Act
        Action act = () => messages.ForOpenAI();

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*Tool*tool result*");
    }

    [Fact]
    public void ForOpenAI_WhenUserMessageHasSeveralTextSegments_EmitsOneContentPartPerSegmentInOrder()
    {
        // Arrange
        IReadOnlyList<ContextMessage> messages =
        [
            new ContextMessage { Role = MessageRole.User, Segments = [new TextContent("RULE-ONE"), new TextContent("RULE-TWO")] },
        ];

        // Act
        var result = messages.ForOpenAI();

        // Assert
        var user = result.Should().ContainSingle().Subject.Should().BeOfType<UserChatMessage>().Subject;
        user.Content.Select(part => part.Text).Should().Equal("RULE-ONE", "RULE-TWO");
    }

    [Fact]
    public void ForOpenAI_WhenSystemMessageHasSeveralTextSegments_EmitsOneContentPartPerSegmentInOrder()
    {
        // Arrange
        IReadOnlyList<ContextMessage> messages =
        [
            new ContextMessage { Role = MessageRole.System, Segments = [new TextContent("A"), new TextContent("B")] },
        ];

        // Act
        var result = messages.ForOpenAI();

        // Assert
        var system = result.Should().ContainSingle().Subject.Should().BeOfType<SystemChatMessage>().Subject;
        system.Content.Select(part => part.Text).Should().Equal("A", "B");
    }

    [Fact]
    public void ForOpenAI_WhenModelMessageHasSeveralTextSegments_EmitsOneContentPartPerSegmentInOrder()
    {
        // Arrange
        IReadOnlyList<ContextMessage> messages =
        [
            new ContextMessage
            {
                Role = MessageRole.Model,
                Segments = [new TextContent("PART-ONE"), new ToolUseContent("call_1", "t", "{}"), new TextContent("PART-TWO")],
            },
            new ContextMessage { Role = MessageRole.Tool, Segments = [new ToolResultContent("call_1", "t", "ok")] },
        ];

        // Act
        var result = messages.ForOpenAI();

        // Assert
        var assistant = result[0].Should().BeOfType<AssistantChatMessage>().Subject;
        assistant.Content.Select(part => part.Text).Should().Equal("PART-ONE", "PART-TWO");
        assistant.ToolCalls.Should().ContainSingle();
    }

    [Fact]
    public void ForOpenAI_WhenPinnedAndModelMessagesHaveSeveralTextSegments_PayloadContainsEveryText()
    {
        // Arrange
        IReadOnlyList<ContextMessage> messages =
        [
            new ContextMessage { Role = MessageRole.User, Segments = [new TextContent("RULE-ONE"), new TextContent("RULE-TWO")] },
            ContextMessage.FromText(MessageRole.User, "question"),
            new ContextMessage { Role = MessageRole.Model, Segments = [new TextContent("PART-ONE"), new TextContent("PART-TWO")] },
            ContextMessage.FromText(MessageRole.User, "next"),
        ];

        // Act
        var result = messages.ForOpenAI();

        // Assert
        var payload = string.Join("|", result.SelectMany(message => message.Content).Select(part => part.Text));
        payload.Should().Contain("RULE-ONE").And.Contain("RULE-TWO").And.Contain("PART-ONE").And.Contain("PART-TWO");
        result.Should().HaveCount(messages.Count);
    }

    [Fact]
    public void ForOpenAI_WhenUserMessageContainsToolUse_ThrowsInvalidOperationExceptionNamingRoleAndSegment()
    {
        // Arrange
        IReadOnlyList<ContextMessage> messages =
        [
            new ContextMessage { Role = MessageRole.User, Segments = [new TextContent("hi"), new ToolUseContent("call_1", "t", "{}")] },
        ];

        // Act
        Action act = () => messages.ForOpenAI();

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*User*ToolUseContent*");
    }

    [Fact]
    public void ForOpenAI_WhenToolMessageMixesTextWithToolResult_ThrowsInvalidOperationException()
    {
        // Arrange
        IReadOnlyList<ContextMessage> messages =
        [
            new ContextMessage { Role = MessageRole.Model, Segments = [new ToolUseContent("call_1", "t", "{}")] },
            new ContextMessage { Role = MessageRole.Tool, Segments = [new ToolResultContent("call_1", "t", "ok"), new TextContent("x")] },
        ];

        // Act
        Action act = () => messages.ForOpenAI();

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*Tool*");
    }

    [Fact]
    public void ForOpenAI_WhenToolMessageHoldsSeveralResults_EmitsOneToolMessagePerResult()
    {
        // Arrange
        IReadOnlyList<ContextMessage> messages =
        [
            new ContextMessage { Role = MessageRole.Model, Segments = [new ToolUseContent("a", "t", "{}"), new ToolUseContent("b", "t", "{}")] },
            new ContextMessage { Role = MessageRole.Tool, Segments = [new ToolResultContent("a", "t", "ra"), new ToolResultContent("b", "t", "rb")] },
        ];

        // Act
        var result = messages.ForOpenAI();

        // Assert
        result.Should().HaveCount(3);
        result[1].Should().BeOfType<ToolChatMessage>().Which.ToolCallId.Should().Be("a");
        result[2].Should().BeOfType<ToolChatMessage>().Which.ToolCallId.Should().Be("b");
    }

    [Fact]
    public void ForOpenAI_WhenModelToolCallsAreUnansweredAtEndOfList_ThrowsInvalidOperationException()
    {
        // Arrange
        IReadOnlyList<ContextMessage> messages =
        [
            ContextMessage.FromText(MessageRole.User, "go"),
            new ContextMessage { Role = MessageRole.Model, Segments = [new ToolUseContent("call_1", "search", "{}")] },
        ];

        // Act
        Action act = () => messages.ForOpenAI();

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*call_1*end of the prepared history*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ResponseSegments_WhenToolCallArgumentsAreEmptyOrWhitespace_UsesEmptyJsonObject(string arguments)
    {
        // Arrange
        var response = CreateChatCompletion(
            toolCalls: [ChatToolCall.CreateFunctionToolCall("call_1", "list_files", BinaryData.FromString(arguments))]);

        // Act
        var result = response.ResponseSegments();

        // Assert
        result.Should().ContainSingle().Which.Should().BeEquivalentTo(new ToolUseContent("call_1", "list_files", "{}"));
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
    public void ForOpenAI_WhenToolResultHasNoPrecedingAssistantToolCall_ThrowsInvalidOperationException()
    {
        // Arrange
        IReadOnlyList<ContextMessage> messages =
        [
            ContextMessage.FromText(MessageRole.Model, "summary"),
            new ContextMessage
            {
                Role = MessageRole.Tool,
                Segments =
                [
                    new ToolResultContent("call_1", "search", "tool result"),
                ],
            },
        ];

        // Act
        Action act = () => messages.ForOpenAI();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*call_1*");
    }

    [Fact]
    public void ForOpenAI_WhenAssistantToolCallIsNotAnsweredBeforeNextUserMessage_ThrowsInvalidOperationException()
    {
        // Arrange
        IReadOnlyList<ContextMessage> messages =
        [
            new ContextMessage
            {
                Role = MessageRole.Model,
                Segments =
                [
                    new ToolUseContent("call_1", "search", "{}"),
                ],
            },
            ContextMessage.FromText(MessageRole.User, "continue"),
        ];

        // Act
        Action act = () => messages.ForOpenAI();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*matching tool results*");
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
