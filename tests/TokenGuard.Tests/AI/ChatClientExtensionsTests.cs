using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.AI;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;
using TokenGuard.Extensions.AI;
using TextContent = TokenGuard.Core.Models.Content.TextContent;
using ToolResultContent = TokenGuard.Core.Models.Content.ToolResultContent;

namespace TokenGuard.Tests.AI;

public sealed class ChatClientExtensionsTests
{
    [Fact]
    public void ForChatClient_MixedHistory_PreservesRolesSegmentsAndStructuredArguments()
    {
        IReadOnlyList<ContextMessage> history =
        [
            ContextMessage.FromContent(MessageRole.System, [new TextContent("policy"), new TextContent("constraints")]),
            ContextMessage.FromText(MessageRole.User, "question"),
            ContextMessage.FromContent(MessageRole.Model,
            [
                new TextContent("before"),
                new ToolUseContent("one", "search", "{\"query\":\"value\",\"limit\":2,\"nested\":{\"ok\":true}}"),
                new TextContent("between"),
                new ToolUseContent("two", "read", "{}"),
            ]),
            ContextMessage.FromContent(MessageRole.Tool,
                [new ToolResultContent("one", "search", "result one"), new ToolResultContent("two", "read", "result two")]),
            ContextMessage.FromText(MessageRole.Model, "answer"),
        ];

        var messages = history.ForChatClient();

        messages.Select(message => message.Role).Should().Equal(
            ChatRole.System, ChatRole.User, ChatRole.Assistant, ChatRole.Tool, ChatRole.Tool, ChatRole.Assistant);
        messages[0].Contents.Cast<Microsoft.Extensions.AI.TextContent>().Select(text => text.Text).Should().Equal("policy", "constraints");
        messages[2].Contents.Should().HaveCount(4);
        messages[2].Contents[2].Should().BeOfType<Microsoft.Extensions.AI.TextContent>().Which.Text.Should().Be("between");
        var call = messages[2].Contents[1].Should().BeOfType<FunctionCallContent>().Subject;
        call.Name.Should().Be("search");
        call.CallId.Should().Be("one");
        ((JsonElement)call.Arguments!["limit"]!).GetInt32().Should().Be(2);
        ((JsonElement)call.Arguments["nested"]!).GetProperty("ok").GetBoolean().Should().BeTrue();
        messages[3].Contents.Single().Should().BeOfType<FunctionResultContent>().Which.Result.Should().Be("result one");
        messages[4].Contents.Single().Should().BeOfType<FunctionResultContent>().Which.CallId.Should().Be("two");
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("\"string\"")]
    [InlineData("{broken")]
    public void ForChatClient_ArgumentsAreNotAJsonObject_Throws(string arguments)
    {
        IReadOnlyList<ContextMessage> history =
        [
            ContextMessage.FromContent(MessageRole.Model, new ToolUseContent("call", "search", arguments)),
            ContextMessage.FromContent(MessageRole.Tool, new ToolResultContent("call", "search", "result")),
        ];

        var act = () => history.ForChatClient();

        act.Should().Throw<InvalidOperationException>().WithMessage("*'call'*JSON object*");
    }

    [Theory]
    [InlineData(MessageRole.System)]
    [InlineData(MessageRole.User)]
    [InlineData(MessageRole.Model)]
    public void ForChatClient_ResultInNonToolRole_Throws(MessageRole role)
    {
        IReadOnlyList<ContextMessage> history = [ContextMessage.FromContent(role, new ToolResultContent("call", "search", "result"))];

        var act = () => history.ForChatClient();

        act.Should().Throw<InvalidOperationException>().WithMessage("*ToolResultContent*");
    }

    [Theory]
    [InlineData(MessageRole.System)]
    [InlineData(MessageRole.User)]
    [InlineData(MessageRole.Tool)]
    public void ForChatClient_CallInNonModelRole_Throws(MessageRole role)
    {
        IReadOnlyList<ContextMessage> history = [ContextMessage.FromContent(role, new ToolUseContent("call", "search", "{}"))];

        var act = () => history.ForChatClient();

        act.Should().Throw<InvalidOperationException>().WithMessage("*ToolUseContent*");
    }

    [Fact]
    public void ForChatClient_TextInToolRole_Throws()
    {
        IReadOnlyList<ContextMessage> history = [ContextMessage.FromText(MessageRole.Tool, "result")];

        var act = () => history.ForChatClient();

        act.Should().Throw<InvalidOperationException>().WithMessage("*TextContent*");
    }

    [Theory]
    [InlineData(MessageRole.System)]
    [InlineData(MessageRole.User)]
    [InlineData(MessageRole.Model)]
    public void ForChatClient_UnknownSegment_Throws(MessageRole role)
    {
        IReadOnlyList<ContextMessage> history = [ContextMessage.FromContent(role, new UnknownSegment("unsupported"))];

        var act = () => history.ForChatClient();

        act.Should().Throw<InvalidOperationException>().WithMessage("*UnknownSegment*");
    }

    [Theory]
    [InlineData(MessageRole.System)]
    [InlineData(MessageRole.User)]
    [InlineData(MessageRole.Model)]
    public void ForChatClient_MessageWithNoSendableText_Throws(MessageRole role)
    {
        IReadOnlyList<ContextMessage> history = [ContextMessage.FromContent(role, new TextContent("valid") { Content = " " })];

        var act = () => history.ForChatClient();

        act.Should().Throw<InvalidOperationException>().WithMessage("*no text*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ForChatClient_UnpairedOrInterruptedToolLoop_Throws(int scenario)
    {
        var call = ContextMessage.FromContent(MessageRole.Model, new ToolUseContent("one", "search", "{}"));
        var result = ContextMessage.FromContent(MessageRole.Tool, new ToolResultContent("one", "search", "result"));
        IReadOnlyList<ContextMessage> history = scenario switch
        {
            0 => [call],
            1 => [result],
            2 => [call, ContextMessage.FromText(MessageRole.User, "interrupted"), result],
            _ => [call, result, result],
        };

        var act = () => history.ForChatClient();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ForChatClient_DuplicateCallIds_Throws()
    {
        var call = new ToolUseContent("one", "search", "{}");
        IReadOnlyList<ContextMessage> history = [ContextMessage.FromContent(MessageRole.Model, [call, call])];

        var act = () => history.ForChatClient();

        act.Should().Throw<InvalidOperationException>().WithMessage("*more than once*");
    }

    [Fact]
    public void ForChatClient_UnknownRole_Throws()
    {
        IReadOnlyList<ContextMessage> history = [ContextMessage.FromText((MessageRole)999, "text")];

        var act = () => history.ForChatClient();

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ResponseSegments_MultipleMessages_PreservesTextAndToolOrderAndOmitsReasoningAndUsage()
    {
        ChatResponse response = new([
            new ChatMessage(ChatRole.Assistant,
            [
                new TextReasoningContent("private reasoning"),
                new Microsoft.Extensions.AI.TextContent("before"),
                new FunctionCallContent("one", "search", new Dictionary<string, object?> { ["query"] = "value", ["limit"] = 2 }),
                new UsageContent(),
            ]),
            new ChatMessage(ChatRole.Assistant,
                [new Microsoft.Extensions.AI.TextContent("after"), new FunctionCallContent("two", "read")]),
        ]);

        var segments = response.ResponseSegments();

        segments.Should().HaveCount(4);
        segments[0].Should().Be(new TextContent("before"));
        segments[2].Should().Be(new TextContent("after"));
        var call = segments[1].Should().BeOfType<ToolUseContent>().Subject;
        using var json = JsonDocument.Parse(call.Content);
        json.RootElement.GetProperty("limit").GetInt32().Should().Be(2);
        call.ToolCallId.Should().Be("one");
        call.ToolName.Should().Be("search");
        segments[3].Content.Should().Be("{}");
        response.TextSegments().Select(text => text.Content).Should().Equal("before", "after");
        response.ToolUseSegments().Select(tool => tool.ToolCallId).Should().Equal("one", "two");
    }

    [Fact]
    public void ResponseSegments_WhitespaceText_IsOmitted()
    {
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, " "));

        var segments = response.ResponseSegments();

        segments.Should().BeEmpty();
    }

    [Fact]
    public void ResponseSegments_UnserializableArguments_Throws()
    {
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("one", "search", new Dictionary<string, object?> { ["type"] = typeof(string) })]));

        var act = () => response.ResponseSegments();

        act.Should().Throw<NotSupportedException>();
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public void SegmentHelpers_FunctionCallMappingFailed_ThrowWithOriginalException(int helper, bool laterMessage)
    {
        var mappingException = new JsonException("Invalid provider arguments.");
        var failedCall = new FunctionCallContent("failed", "search") { Exception = mappingException };
        List<ChatMessage> messages = laterMessage ? [new(ChatRole.Assistant, [new FunctionCallContent("valid", "read")])] : [];
        messages.Add(new ChatMessage(ChatRole.Assistant, [failedCall]));
        var response = new ChatResponse(messages);

        Action act = helper switch
        {
            0 => () => response.ResponseSegments(),
            1 => () => response.TextSegments(),
            _ => () => response.ToolUseSegments(),
        };

        act.Should().Throw<InvalidOperationException>().WithMessage("*'failed'*mapping failed*")
            .Which.InnerException.Should().BeSameAs(mappingException);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public void SegmentHelpers_InformationalFunctionCall_ThrowInsteadOfExposingAnExecutableRequest(int helper, bool laterMessage)
    {
        var informationalCall = new FunctionCallContent("processed", "search") { InformationalOnly = true };
        List<ChatMessage> messages = laterMessage ? [new(ChatRole.Assistant, [new FunctionCallContent("valid", "read")])] : [];
        messages.Add(new ChatMessage(ChatRole.Assistant, [informationalCall]));
        var response = new ChatResponse(messages);

        Action act = helper switch
        {
            0 => () => response.ResponseSegments(),
            1 => () => response.TextSegments(),
            _ => () => response.ToolUseSegments(),
        };

        act.Should().Throw<InvalidOperationException>().WithMessage("*'processed'*informational*executable tool request*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void SegmentHelpers_UnsupportedContent_ThrowWithTypeName(int helper)
    {
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, [new UriContent("https://example.com/image.png", "image/png")]));

        Action act = helper switch
        {
            0 => () => response.ResponseSegments(),
            1 => () => response.TextSegments(),
            _ => () => response.ToolUseSegments(),
        };

        act.Should().Throw<NotSupportedException>().WithMessage("*UriContent*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ResponseHelpers_AutomaticToolLoop_ThrowsWithManualLoopGuidance(int helper)
    {
        var response = new ChatResponse([
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("one", "search")]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("one", "result")]),
            new ChatMessage(ChatRole.Assistant, "answer"),
        ]);

        Action act = helper switch
        {
            0 => () => response.ResponseSegments(),
            1 => () => response.TextSegments(),
            2 => () => response.ToolUseSegments(),
            _ => () => response.InputTokens(),
        };

        act.Should().Throw<InvalidOperationException>().WithMessage("*tool loop in the caller*automatic function invocation*");
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(0L, null)]
    [InlineData(-1L, null)]
    [InlineData(123L, 123)]
    [InlineData(long.MaxValue, int.MaxValue)]
    public void InputTokens_ReportedCount_IsPositiveAndBounded(long? reported, int? expected)
    {
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer"))
        {
            Usage = reported.HasValue ? new UsageDetails { InputTokenCount = reported } : null,
        };

        var tokens = response.InputTokens();

        tokens.Should().Be(expected);
    }

    [Fact]
    public void ForChatClient_NullHistory_Throws()
    {
        var act = () => TokenGuard.Extensions.AI.ChatClientExtensions.ForChatClient(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ResponseHelpers_NullResponse_Throws(int helper)
    {
        ChatResponse response = null!;
        Action act = helper switch
        {
            0 => () => response.ResponseSegments(),
            1 => () => response.TextSegments(),
            2 => () => response.ToolUseSegments(),
            _ => () => response.InputTokens(),
        };

        act.Should().Throw<ArgumentNullException>();
    }

    private sealed record UnknownSegment(string Value) : ContentSegment(Value);
}
