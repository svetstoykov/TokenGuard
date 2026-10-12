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
    private const string MaskedPlaceholder =
        "[Tool result removed to save context — search, call_masked. Call the tool again if you need this content.]";

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
                Segments =
                [
                    new ToolUseContent("call_1", "read_file", "{}"),
                ],
            },
            new ContextMessage { Role = MessageRole.Tool, Segments = [new ToolResultContent("call_1", "read_file", "ok")] },
        ];

        // Act
        var (result, system) = messages.ForAnthropic();

        // Assert
        system.Should().BeNull();
        result.Should().HaveCount(2);
        result[0].Role.Value().Should().Be(Role.Assistant);
        result[0].Content.TryPickContentBlockParams(out var assistantBlocks).Should().BeTrue();
        assistantBlocks.Should().ContainSingle();
        assistantBlocks![0].TryPickToolUse(out _).Should().BeTrue();
    }

    [Fact]
    public void ForAnthropic_WhenUserMessageHasSeveralTextSegments_EmitsOneTextBlockPerSegmentInOrder()
    {
        // Arrange
        IReadOnlyList<ContextMessage> messages =
        [
            new ContextMessage { Role = MessageRole.User, Segments = [new TextContent("RULE-ONE"), new TextContent("RULE-TWO")] },
        ];

        // Act
        var (result, _) = messages.ForAnthropic();

        // Assert
        var user = result.Should().ContainSingle().Subject;
        user.Role.Value().Should().Be(Role.User);
        Texts(user).Should().Equal("RULE-ONE", "RULE-TWO");
    }

    [Fact]
    public void ForAnthropic_WhenSystemMessageHasSeveralTextSegments_EmitsOneSystemBlockPerSegmentInOrder()
    {
        // Arrange
        IReadOnlyList<ContextMessage> messages =
        [
            new ContextMessage { Role = MessageRole.System, Segments = [new TextContent("A"), new TextContent("B")] },
            ContextMessage.FromText(MessageRole.User, "question"),
        ];

        // Act
        var (result, system) = messages.ForAnthropic();

        // Assert
        SystemTexts(system).Should().Equal("A", "B");
        result.Should().ContainSingle();
    }

    [Fact]
    public void ForAnthropic_WhenSeveralSystemMessagesArePresent_EmitsEverySystemTextInOrderOutsideTheMessageList()
    {
        // Arrange
        IReadOnlyList<ContextMessage> messages =
        [
            ContextMessage.FromText(MessageRole.System, "first"),
            ContextMessage.FromText(MessageRole.User, "question"),
            ContextMessage.FromText(MessageRole.System, "second"),
        ];

        // Act
        var (result, system) = messages.ForAnthropic();

        // Assert
        SystemTexts(system).Should().Equal("first", "second");
        result.Should().ContainSingle().Which.Role.Value().Should().Be(Role.User);
    }

    [Fact]
    public void ForAnthropic_WhenModelMessageHasSeveralTextSegments_EmitsOneTextBlockPerSegmentInOrder()
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
        var (result, _) = messages.ForAnthropic();

        // Assert
        Texts(result[0]).Should().Equal("PART-ONE", "PART-TWO");
        ToolUseIds(result[0]).Should().Equal("call_1");
    }

    [Fact]
    public void ForAnthropic_WhenTextSegmentIsWhitespaceBesideOtherText_OmitsTheWhitespaceSegment()
    {
        // Arrange
        IReadOnlyList<ContextMessage> messages =
        [
            new ContextMessage { Role = MessageRole.User, Segments = [new TextContent("kept"), new TextContent("x") with { Content = "  " }] },
        ];

        // Act
        var (result, _) = messages.ForAnthropic();

        // Assert
        Texts(result.Should().ContainSingle().Subject).Should().Equal("kept");
    }

    [Fact]
    public void ForAnthropic_WhenPinnedAndModelMessagesHaveSeveralTextSegments_PayloadContainsEveryText()
    {
        // Arrange
        IReadOnlyList<ContextMessage> messages =
        [
            new ContextMessage
            {
                Role = MessageRole.User,
                IsPinned = true,
                Segments = [new TextContent("RULE-ONE"), new TextContent("RULE-TWO")],
            },
            ContextMessage.FromText(MessageRole.User, "question"),
            new ContextMessage { Role = MessageRole.Model, Segments = [new TextContent("PART-ONE"), new TextContent("PART-TWO")] },
            ContextMessage.FromText(MessageRole.User, "next"),
        ];

        // Act
        var (result, _) = messages.ForAnthropic();

        // Assert
        result.SelectMany(Texts).Should().Equal("RULE-ONE", "RULE-TWO", "question", "PART-ONE", "PART-TWO", "next");
        result.Should().HaveCount(messages.Count);
    }

    [Fact]
    public void ForAnthropic_WhenConsecutiveMessagesShareARole_EmitsOneMessagePerInputMessageInOrder()
    {
        // Arrange
        IReadOnlyList<ContextMessage> messages =
        [
            new ContextMessage { Role = MessageRole.User, IsPinned = true, Segments = [new TextContent("pinned")] },
            ContextMessage.FromText(MessageRole.User, "first"),
            ContextMessage.FromText(MessageRole.User, "second"),
            ContextMessage.FromText(MessageRole.Model, "answer one"),
            ContextMessage.FromText(MessageRole.Model, "answer two"),
        ];

        // Act
        var (result, _) = messages.ForAnthropic();

        // Assert
        result.Select(message => message.Role.Value()).Should().Equal(Role.User, Role.User, Role.User, Role.Assistant, Role.Assistant);
        result.Select(message => Texts(message).Single()).Should().Equal("pinned", "first", "second", "answer one", "answer two");
    }

    [Fact]
    public void ForAnthropic_WhenHistoryStartsWithSummaryMessage_EmitsSummaryAsAssistantText()
    {
        // Arrange
        IReadOnlyList<ContextMessage> messages =
        [
            new ContextMessage
            {
                Role = MessageRole.Model,
                State = CompactionState.Summarized,
                Segments = [new TextContent("summary of earlier turns")],
            },
            ContextMessage.FromText(MessageRole.User, "continue"),
        ];

        // Act
        var (result, system) = messages.ForAnthropic();

        // Assert
        system.Should().BeNull();
        result.Should().HaveCount(2);
        result[0].Role.Value().Should().Be(Role.Assistant);
        Texts(result[0]).Should().Equal("summary of earlier turns");
        result[1].Role.Value().Should().Be(Role.User);
    }

    [Fact]
    public void ForAnthropic_WhenModelMessageHasSeveralToolCalls_EmitsOneToolUseBlockPerCallInOrder()
    {
        // Arrange
        IReadOnlyList<ContextMessage> messages =
        [
            new ContextMessage
            {
                Role = MessageRole.Model,
                Segments = [new ToolUseContent("a", "search", "{\"q\":\"one\"}"), new ToolUseContent("b", "fetch", "{\"id\":2}")],
            },
            new ContextMessage { Role = MessageRole.Tool, Segments = [new ToolResultContent("a", "search", "ra")] },
            new ContextMessage { Role = MessageRole.Tool, Segments = [new ToolResultContent("b", "fetch", "rb")] },
        ];

        // Act
        var (result, _) = messages.ForAnthropic();

        // Assert
        result.Should().HaveCount(3);
        ToolUseIds(result[0]).Should().Equal("a", "b");
        ToolResults(result[1]).Should().Equal(("a", "ra"));
        ToolResults(result[2]).Should().Equal(("b", "rb"));
    }

    [Fact]
    public void ForAnthropic_WhenToolMessageHoldsSeveralResults_EmitsOneUserMessageWithEveryResultInOrder()
    {
        // Arrange
        IReadOnlyList<ContextMessage> messages =
        [
            new ContextMessage { Role = MessageRole.Model, Segments = [new ToolUseContent("a", "t", "{}"), new ToolUseContent("b", "t", "{}")] },
            new ContextMessage { Role = MessageRole.Tool, Segments = [new ToolResultContent("a", "t", "ra"), new ToolResultContent("b", "t", "rb")] },
        ];

        // Act
        var (result, _) = messages.ForAnthropic();

        // Assert
        result.Should().HaveCount(2);
        result[1].Role.Value().Should().Be(Role.User);
        ToolResults(result[1]).Should().Equal(("a", "ra"), ("b", "rb"));
    }

    [Fact]
    public void ForAnthropic_WhenToolMessageIsMasked_EmitsToolResultWithPlaceholderText()
    {
        // Arrange
        IReadOnlyList<ContextMessage> messages =
        [
            new ContextMessage { Role = MessageRole.Model, Segments = [new ToolUseContent("call_masked", "search", "{\"query\":\"token guard\"}")] },
            new ContextMessage
            {
                Role = MessageRole.Tool,
                State = CompactionState.Masked,
                Segments = [new ToolResultContent("call_masked", "search", MaskedPlaceholder)],
            },
        ];

        // Act
        var (result, _) = messages.ForAnthropic();

        // Assert
        result.Should().HaveCount(2);
        ToolUseIds(result[0]).Should().Equal("call_masked");
        ToolResults(result[1]).Should().Equal(("call_masked", MaskedPlaceholder));
    }

    [Fact]
    public void ForAnthropic_WhenMaskedAndUnmaskedToolMessagesAnswerOneTurn_EmitsToolResultForEachCall()
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
                Segments = [new ToolResultContent("call_masked", "search", MaskedPlaceholder)],
            },
            new ContextMessage { Role = MessageRole.Tool, Segments = [new ToolResultContent("call_unmasked", "fetch", "{\"value\":42}")] },
        ];

        // Act
        var (result, _) = messages.ForAnthropic();

        // Assert
        ToolUseIds(result[0]).Should().Equal("call_masked", "call_unmasked");
        result.Skip(1).SelectMany(ToolResults).Should().Equal(("call_masked", MaskedPlaceholder), ("call_unmasked", "{\"value\":42}"));
    }

    [Fact]
    public void ForAnthropic_WhenUserMessageHasNoText_ThrowsInvalidOperationExceptionNamingRoleAndSegment()
    {
        // Arrange
        IReadOnlyList<ContextMessage> messages =
        [
            new ContextMessage { Role = MessageRole.Model, Segments = [new ToolUseContent("call_1", "t", "{}")] },
            new ContextMessage { Role = MessageRole.Tool, Segments = [new ToolResultContent("call_1", "t", "ok")] },
            new ContextMessage { Role = MessageRole.User, Segments = [new ToolResultContent("call_1", "t", "ok")] },
        ];

        // Act
        Action act = () => messages.ForAnthropic();

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*User*Anthropic*ToolResultContent*");
    }

    [Fact]
    public void ForAnthropic_WhenUserMessageTextIsAllWhitespace_ThrowsInvalidOperationExceptionNamingRole()
    {
        // Arrange
        IReadOnlyList<ContextMessage> messages =
        [
            new ContextMessage { Role = MessageRole.User, Segments = [new TextContent("x") with { Content = " " }] },
        ];

        // Act
        Action act = () => messages.ForAnthropic();

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*User*Anthropic*no text*");
    }

    [Fact]
    public void ForAnthropic_WhenUserMessageContainsToolUse_ThrowsInvalidOperationExceptionNamingRoleAndSegment()
    {
        // Arrange
        IReadOnlyList<ContextMessage> messages =
        [
            new ContextMessage { Role = MessageRole.User, Segments = [new TextContent("hi"), new ToolUseContent("call_1", "t", "{}")] },
        ];

        // Act
        Action act = () => messages.ForAnthropic();

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*User*Anthropic*ToolUseContent*");
    }

    [Fact]
    public void ForAnthropic_WhenModelMessageContainsToolResult_ThrowsInvalidOperationExceptionNamingRoleAndSegment()
    {
        // Arrange
        IReadOnlyList<ContextMessage> messages =
        [
            new ContextMessage { Role = MessageRole.Model, Segments = [new ToolResultContent("call_1", "t", "ok")] },
        ];

        // Act
        Action act = () => messages.ForAnthropic();

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*Model*Anthropic*ToolResultContent*");
    }

    [Fact]
    public void ForAnthropic_WhenModelMessageHasNeitherTextNorToolCall_ThrowsInvalidOperationExceptionNamingRole()
    {
        // Arrange
        IReadOnlyList<ContextMessage> messages =
        [
            new ContextMessage { Role = MessageRole.Model, Segments = [new TextContent("x") with { Content = string.Empty }] },
        ];

        // Act
        Action act = () => messages.ForAnthropic();

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*Model*Anthropic*neither text nor a tool call*");
    }

    [Fact]
    public void ForAnthropic_WhenSystemMessageContainsNonTextSegment_ThrowsInvalidOperationExceptionNamingRoleAndSegment()
    {
        // Arrange
        IReadOnlyList<ContextMessage> messages =
        [
            new ContextMessage { Role = MessageRole.System, Segments = [new TextContent("rules"), new ToolResultContent("call_1", "t", "ok")] },
        ];

        // Act
        Action act = () => messages.ForAnthropic();

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*System*Anthropic*ToolResultContent*");
    }

    [Fact]
    public void ForAnthropic_WhenSystemMessageTextIsAllWhitespace_ThrowsInvalidOperationExceptionNamingRole()
    {
        // Arrange
        IReadOnlyList<ContextMessage> messages =
        [
            new ContextMessage { Role = MessageRole.System, Segments = [new TextContent("x") with { Content = " " }] },
        ];

        // Act
        Action act = () => messages.ForAnthropic();

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*System*Anthropic*no text*");
    }

    [Fact]
    public void ForAnthropic_WhenToolMessageHasNoToolResult_ThrowsInvalidOperationExceptionNamingRole()
    {
        // Arrange
        IReadOnlyList<ContextMessage> messages = [ContextMessage.FromText(MessageRole.Tool, "NOTE-THREE")];

        // Act
        Action act = () => messages.ForAnthropic();

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*Tool*Anthropic*tool result*");
    }

    [Fact]
    public void ForAnthropic_WhenToolMessageMixesTextWithToolResult_ThrowsInvalidOperationExceptionNamingRole()
    {
        // Arrange
        IReadOnlyList<ContextMessage> messages =
        [
            new ContextMessage { Role = MessageRole.Model, Segments = [new ToolUseContent("call_1", "t", "{}")] },
            new ContextMessage { Role = MessageRole.Tool, Segments = [new ToolResultContent("call_1", "t", "ok"), new TextContent("x")] },
        ];

        // Act
        Action act = () => messages.ForAnthropic();

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*Tool*Anthropic*tool result*");
    }

    [Theory]
    [InlineData("[1,2]")]
    [InlineData("\"text\"")]
    [InlineData("null")]
    [InlineData("{not json")]
    public void ForAnthropic_WhenToolCallArgumentsAreNotAJsonObject_ThrowsInvalidOperationExceptionNamingToolCall(string arguments)
    {
        // Arrange
        IReadOnlyList<ContextMessage> messages =
        [
            new ContextMessage { Role = MessageRole.Model, Segments = [new ToolUseContent("call_1", "search", arguments)] },
            new ContextMessage { Role = MessageRole.Tool, Segments = [new ToolResultContent("call_1", "search", "ok")] },
        ];

        // Act
        Action act = () => messages.ForAnthropic();

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*Model*Anthropic*call_1*JSON object*");
    }

    [Fact]
    public void ForAnthropic_WhenToolResultHasNoPrecedingAssistantToolCall_ThrowsInvalidOperationException()
    {
        // Arrange
        IReadOnlyList<ContextMessage> messages =
        [
            ContextMessage.FromText(MessageRole.Model, "summary"),
            new ContextMessage { Role = MessageRole.Tool, Segments = [new ToolResultContent("call_1", "search", "tool result")] },
        ];

        // Act
        Action act = () => messages.ForAnthropic();

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*call_1*no preceding assistant tool call*");
    }

    [Fact]
    public void ForAnthropic_WhenAssistantToolCallIsNotAnsweredBeforeNextUserMessage_ThrowsInvalidOperationException()
    {
        // Arrange
        IReadOnlyList<ContextMessage> messages =
        [
            new ContextMessage { Role = MessageRole.Model, Segments = [new ToolUseContent("call_1", "search", "{}")] },
            ContextMessage.FromText(MessageRole.User, "continue"),
        ];

        // Act
        Action act = () => messages.ForAnthropic();

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*matching tool results*User*");
    }

    [Fact]
    public void ForAnthropic_WhenModelToolCallsAreUnansweredAtEndOfList_ThrowsInvalidOperationException()
    {
        // Arrange
        IReadOnlyList<ContextMessage> messages =
        [
            ContextMessage.FromText(MessageRole.User, "go"),
            new ContextMessage { Role = MessageRole.Model, Segments = [new ToolUseContent("call_1", "search", "{}")] },
        ];

        // Act
        Action act = () => messages.ForAnthropic();

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*call_1*end of the prepared history*");
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
    public void ForAnthropic_WhenRecordedResponseHasSeveralTextBlocksAndToolCalls_SendsEveryBlockBack()
    {
        // Arrange
        var response = CreateMessage(
            textParts: ["first", "second"],
            toolUses:
            [
                ("call_1", "search", "{\"q\":\"one\"}"),
                ("call_2", "fetch", "{\"id\":2}"),
            ]);
        IReadOnlyList<ContextMessage> messages =
        [
            ContextMessage.FromText(MessageRole.User, "go"),
            new ContextMessage { Role = MessageRole.Model, Segments = response.ResponseSegments() },
            new ContextMessage { Role = MessageRole.Tool, Segments = [new ToolResultContent("call_1", "search", "r1")] },
            new ContextMessage { Role = MessageRole.Tool, Segments = [new ToolResultContent("call_2", "fetch", "r2")] },
        ];

        // Act
        var (result, _) = messages.ForAnthropic();

        // Assert
        Texts(result[1]).Should().Equal("first", "second");
        ToolUseIds(result[1]).Should().Equal("call_1", "call_2");
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
    public void InputTokens_WhenUsageReportsCachedTokens_ReturnsSumOfUncachedCacheCreationAndCacheReadTokens()
    {
        // Arrange
        var response = CreateMessage(inputTokenCount: 12, cacheCreationInputTokens: 300, cacheReadInputTokens: 9_000);

        // Act
        var result = response.InputTokens();

        // Assert
        result.Should().Be(9_312);
    }

    [Fact]
    public void InputTokens_WhenUsageIsMissing_ReturnsNull()
    {
        // Arrange
        var response = CreateMessage(inputTokenCount: null);

        // Act
        var result = response.InputTokens();

        // Assert
        result.Should().BeNull();
    }

    private static IReadOnlyList<ContentBlockParam> Blocks(MessageParam message)
    {
        message.Content.TryPickContentBlockParams(out var blocks).Should().BeTrue();
        return blocks!;
    }

    private static List<string> Texts(MessageParam message) =>
        Blocks(message).Select(block => block.TryPickText(out var text) ? text.Text : null).OfType<string>().ToList();

    private static List<string> ToolUseIds(MessageParam message) =>
        Blocks(message).Select(block => block.TryPickToolUse(out var toolUse) ? toolUse.ID : null).OfType<string>().ToList();

    private static List<(string ToolUseId, string Content)> ToolResults(MessageParam message)
    {
        List<(string ToolUseId, string Content)> results = [];

        foreach (var block in Blocks(message))
        {
            block.TryPickToolResult(out var toolResult).Should().BeTrue();
            toolResult!.Content!.TryPickString(out var content).Should().BeTrue();
            results.Add((toolResult.ToolUseID, content!));
        }

        return results;
    }

    private static List<string> SystemTexts(MessageCreateParamsSystem? system)
    {
        system.Should().NotBeNull();
        system!.TryPickTextBlockParams(out var blocks).Should().BeTrue();
        return blocks!.Select(block => block.Text).ToList();
    }

    /// <summary>
    /// Creates an Anthropic message payload for extension method tests.
    /// </summary>
    /// <param name="textParts">The text blocks to include in the assistant message.</param>
    /// <param name="toolUses">The tool use blocks to attach to the assistant message.</param>
    /// <param name="inputTokenCount">The optional uncached input token count; usage metadata is left out when it is null.</param>
    /// <param name="cacheCreationInputTokens">The optional count of input tokens written to the prompt cache.</param>
    /// <param name="cacheReadInputTokens">The optional count of input tokens read from the prompt cache.</param>
    /// <returns>An Anthropic message instance deserialized from a synthetic payload.</returns>
    private static Message CreateMessage(
        IReadOnlyList<string>? textParts = null,
        IReadOnlyList<(string Id, string Name, string InputJson)>? toolUses = null,
        int? inputTokenCount = null,
        int? cacheCreationInputTokens = null,
        int? cacheReadInputTokens = null)
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
                ["cache_creation_input_tokens"] = cacheCreationInputTokens,
                ["cache_read_input_tokens"] = cacheReadInputTokens,
                ["output_tokens"] = 3,
            };
        }

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        return Message.FromRawUnchecked(document.RootElement.Deserialize<Dictionary<string, JsonElement>>()!);
    }
}
