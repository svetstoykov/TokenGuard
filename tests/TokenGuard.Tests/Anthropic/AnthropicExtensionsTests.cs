using Anthropic.Models.Messages;
using FluentAssertions;
using System.Text.Json;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;
using TokenGuard.Extensions.Anthropic;

namespace TokenGuard.Tests.Anthropic;

public sealed class AnthropicExtensionsTests
{
    [Fact]
    public void ForAnthropic_WhenMessagesContainEachSupportedRole_ConvertsMessagesInOrder()
    {
        // Arrange
        IReadOnlyList<ContextMessage> messages =
        [
            ContextMessage.FromText(MessageRole.System, "system prompt"),
            ContextMessage.FromText(MessageRole.User, "user input"),
            new ContextMessage
            {
                Role = MessageRole.Model,
                Content =
                [
                    new TextContent("assistant reply"),
                    new ToolUseContent("call_1", "search", "{\"query\":\"token guard\"}"),
                ],
            },
            new ContextMessage
            {
                Role = MessageRole.Tool,
                Content =
                [
                    new ToolResultContent("call_1", "search", "search result"),
                ],
            },
        ];

        // Act
        var (result, system) = messages.ForAnthropic();

        // Assert
        system.Should().NotBeNull();
        system!.TryPickTextBlockParams(out var systemBlocks).Should().BeTrue();
        systemBlocks.Should().ContainSingle();
        systemBlocks![0].Text.Should().Be("system prompt");

        result.Should().HaveCount(3);

        result[0].Role.Value().Should().Be(Role.User);
        result[0].Content.TryPickContentBlockParams(out var userBlocks).Should().BeTrue();
        var userBlock = userBlocks!.Should().ContainSingle().Subject;
        userBlock.TryPickText(out var userText).Should().BeTrue();
        userText.Should().NotBeNull();
        userText.Text.Should().Be("user input");

        result[1].Role.Value().Should().Be(Role.Assistant);
        result[1].Content.TryPickContentBlockParams(out var assistantBlocks).Should().BeTrue();
        assistantBlocks.Should().HaveCount(2);
        assistantBlocks![0].TryPickText(out var assistantText).Should().BeTrue();
        assistantText.Should().NotBeNull();
        assistantText.Text.Should().Be("assistant reply");
        assistantBlocks[1].TryPickToolUse(out var assistantToolUse).Should().BeTrue();
        assistantToolUse.Should().NotBeNull();
        assistantToolUse.ID.Should().Be("call_1");
        assistantToolUse.Name.Should().Be("search");
        assistantToolUse.Input.Should().ContainKey("query");
        assistantToolUse.Input["query"].GetString().Should().Be("token guard");

        result[2].Role.Value().Should().Be(Role.User);
        result[2].Content.TryPickContentBlockParams(out var toolResultBlocks).Should().BeTrue();
        var toolResultBlock = toolResultBlocks!.Should().ContainSingle().Subject;
        toolResultBlock.TryPickToolResult(out var toolResult).Should().BeTrue();
        toolResult.Should().NotBeNull();
        toolResult.ToolUseID.Should().Be("call_1");
        toolResult.Content.Should().NotBeNull();
        toolResult.Content.Value.Should().Be("search result");
    }

    [Fact]
    public void ForAnthropic_WhenModelMessageContainsOnlyToolUse_OmitsAssistantTextBlock()
    {
        // Arrange
        IReadOnlyList<ContextMessage> messages =
        [
            new ContextMessage
            {
                Role = MessageRole.Model,
                Content =
                [
                    new ToolUseContent("call_1", "read_file", "{}"),
                ],
            },
        ];

        // Act
        var (result, system) = messages.ForAnthropic();

        // Assert
        system.Should().BeNull();
        result.Should().ContainSingle();
        result[0].Role.Value().Should().Be(Role.Assistant);
        result[0].Content.TryPickContentBlockParams(out var assistantBlocks).Should().BeTrue();
        assistantBlocks.Should().ContainSingle();
        assistantBlocks![0].TryPickToolUse(out _).Should().BeTrue();
    }

    [Fact]
    public void ForAnthropic_WhenMessagesIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        IReadOnlyList<ContextMessage> messages = null!;

        // Act
        Action act = () => messages.ForAnthropic();

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ResponseSegments_WhenResponseContainsTextAndToolCalls_ReturnsCombinedSegments()
    {
        // Arrange
        var response = CreateMessage(
            textParts: ["first", "second"],
            toolUses:
            [
                ("call_1", "search", "{\"q\":\"one\"}"),
                ("call_2", "fetch", "{\"id\":2}"),
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
        var response = CreateMessage(textParts: ["useful", " ", "\t", string.Empty, "done"]);

        // Act
        var result = response.TextSegments();

        // Assert
        result.Should().BeEquivalentTo([new TextContent("useful"), new TextContent("done")]);
    }

    [Fact]
    public void ToolUseSegments_WhenResponseContainsToolCalls_MapsEachToolCall()
    {
        // Arrange
        var response = CreateMessage(
            toolUses:
            [
                ("call_1", "search", "{\"q\":\"one\"}"),
                ("call_2", "fetch", "{\"id\":2}"),
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
        var response = CreateMessage(inputTokenCount: 42);

        // Act
        var result = response.InputTokens();

        // Assert
        result.Should().Be(42);
    }

    [Fact]
    public void InputTokens_WhenUsageIsMissing_ReturnsNull()
    {
        // Arrange
        var response = CreateMessage(inputTokenCount: null);

        // Act
        Func<int?> act = response.InputTokens;

        // Assert
        act.Should().Throw<global::Anthropic.Exceptions.AnthropicInvalidDataException>()
            .WithMessage("'usage' cannot be absent");
    }

    /// <summary>
    /// Creates an Anthropic message payload for extension method tests.
    /// </summary>
    /// <param name="textParts">The text blocks to include in the assistant message.</param>
    /// <param name="toolUses">The tool use blocks to attach to the assistant message.</param>
    /// <param name="inputTokenCount">The optional input token count to include in usage metadata.</param>
    /// <returns>An Anthropic message instance deserialized from a synthetic payload.</returns>
    private static Message CreateMessage(
        IReadOnlyList<string>? textParts = null,
        IReadOnlyList<(string Id, string Name, string InputJson)>? toolUses = null,
        int? inputTokenCount = null)
    {
        List<Dictionary<string, object?>> content = [];

        foreach (var text in textParts ?? [])
        {
            content.Add(new Dictionary<string, object?>
            {
                ["type"] = "text",
                ["text"] = text,
            });
        }

        foreach (var toolUse in toolUses ?? [])
        {
            content.Add(new Dictionary<string, object?>
            {
                ["type"] = "tool_use",
                ["id"] = toolUse.Id,
                ["name"] = toolUse.Name,
                ["input"] = JsonSerializer.Deserialize<object>(toolUse.InputJson),
            });
        }

        var payload = new Dictionary<string, object?>
        {
            ["id"] = "msg_test",
            ["type"] = "message",
            ["role"] = "assistant",
            ["model"] = "claude-test",
            ["content"] = content,
            ["stop_reason"] = "end_turn",
            ["stop_sequence"] = null,
        };

        if (inputTokenCount is int tokens)
        {
            payload["usage"] = new Dictionary<string, object?>
            {
                ["input_tokens"] = tokens,
                ["output_tokens"] = 3,
            };
        }

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        return Message.FromRawUnchecked(document.RootElement.Deserialize<Dictionary<string, JsonElement>>()!);
    }
}
