using SemanticFold.Core.Enums;
using SemanticFold.Core.Models;
using SemanticFold.Core.Models.Content;

namespace SemanticFold.Tests.Models;

public sealed class MessageTests
{
    [Fact]
    public void Message_RequiresRoleAndContent()
    {
        var now = DateTimeOffset.UtcNow;
        var message = new Message
        {
            Role = MessageRole.User,
            Content = [new TextContent("hello")],
        };

        Assert.Equal(MessageRole.User, message.Role);
        Assert.Single(message.Content);
        Assert.Equal(CompactionState.Original, message.State);
        Assert.Null(message.TokenCount);
        Assert.True(message.Timestamp >= now.AddSeconds(-1));
        Assert.True(message.Timestamp <= DateTimeOffset.UtcNow.AddSeconds(1));
    }

    [Fact]
    public void Message_RejectsNullContent()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _ = new Message
            {
                Role = MessageRole.User,
                Content = null!,
            });
    }

    [Fact]
    public void Message_RejectsEmptyContent()
    {
        Assert.Throws<ArgumentException>(() =>
            _ = new Message
            {
                Role = MessageRole.User,
                Content = [],
            });
    }

    [Fact]
    public void Message_ContentListIsImmutable()
    {
        var source = new List<ContentBlock> { new TextContent("first") };
        var message = new Message
        {
            Role = MessageRole.User,
            Content = source,
        };

        source.Add(new TextContent("second"));

        Assert.Single(message.Content);
        Assert.Equal("first", Assert.IsType<TextContent>(message.Content[0]).Text);
    }

    [Fact]
    public void Message_FromText_CreatesTextMessage()
    {
        var message = Message.FromText(MessageRole.User, "hello");

        Assert.Equal(MessageRole.User, message.Role);
        Assert.Single(message.Content);
        Assert.Equal("hello", Assert.IsType<TextContent>(message.Content[0]).Text);
        Assert.Equal(CompactionState.Original, message.State);
        Assert.Null(message.TokenCount);
    }

    [Fact]
    public void Message_FromText_RejectsNullOrWhitespace()
    {
        Assert.Throws<ArgumentException>(() => Message.FromText(MessageRole.User, null!));
        Assert.Throws<ArgumentException>(() => Message.FromText(MessageRole.User, string.Empty));
        Assert.Throws<ArgumentException>(() => Message.FromText(MessageRole.User, "   "));
    }

    [Fact]
    public void Message_FromContent_CreatesSingleBlockMessage()
    {
        var block = new ToolUseContent("call_1", "read_file", "{}");
        var message = Message.FromContent(MessageRole.Model, block);

        Assert.Equal(MessageRole.Model, message.Role);
        Assert.Single(message.Content);
        Assert.Same(block, message.Content[0]);
        Assert.Equal(CompactionState.Original, message.State);
        Assert.Null(message.TokenCount);
    }

    [Fact]
    public void Message_WithModifiedState_PreservesOtherProperties()
    {
        var original = new Message
        {
            Role = MessageRole.Model,
            Content =
            [
                new TextContent("Thinking"),
                new ToolUseContent("call_1", "read_file", "{}"),
            ],
            Timestamp = DateTimeOffset.UtcNow.AddMinutes(-1),
            TokenCount = 42,
        };

        var modified = original with { State = CompactionState.Masked };

        Assert.Equal(CompactionState.Masked, modified.State);
        Assert.Equal(original.Role, modified.Role);
        Assert.Equal(original.Content, modified.Content);
        Assert.Equal(original.Timestamp, modified.Timestamp);
        Assert.Equal(original.TokenCount, modified.TokenCount);
    }

    [Fact]
    public void Message_MixedContent_AssistantWithTextAndToolUse()
    {
        var message = new Message
        {
            Role = MessageRole.Model,
            Content =
            [
                new TextContent("I will call a tool."),
                new ToolUseContent("call_1", "web_search", "{\"query\":\"semantic folding\"}"),
            ],
        };

        Assert.Equal(MessageRole.Model, message.Role);
        Assert.Equal(2, message.Content.Count);
        Assert.IsType<TextContent>(message.Content[0]);
        Assert.IsType<ToolUseContent>(message.Content[1]);
    }
}
