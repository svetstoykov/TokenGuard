using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;

namespace TokenGuard.Tests.Models;

public sealed class ContextMessageTests
{
    [Fact]
    public void Message_RequiresRoleAndContent()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var message = new ContextMessage
        {
            Role = MessageRole.User,
            Segments = [new TextContent("hello")],
        };

        // Act

        // Assert
        Assert.Equal(MessageRole.User, message.Role);
        Assert.Single(message.Segments);
        Assert.Equal(CompactionState.Original, message.State);
        Assert.Null(message.TokenCount);
        Assert.True(message.Timestamp >= now.AddSeconds(-1));
        Assert.True(message.Timestamp <= DateTimeOffset.UtcNow.AddSeconds(1));
    }

    [Fact]
    public void Message_RejectsNullContent()
    {
        // Arrange

        // Act
        Action act = () =>
            _ = new ContextMessage
            {
                Role = MessageRole.User,
                Segments = null!,
            };

        // Assert
        Assert.Throws<ArgumentNullException>(act);
    }

    [Fact]
    public void Message_RejectsEmptyContent()
    {
        // Arrange

        // Act
        Action act = () =>
            _ = new ContextMessage
            {
                Role = MessageRole.User,
                Segments = [],
            };

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void Message_ContentListIsImmutable()
    {
        // Arrange
        var source = new List<ContentSegment> { new TextContent("first") };
        var message = new ContextMessage
        {
            Role = MessageRole.User,
            Segments = source,
        };

        // Act
        source.Add(new TextContent("second"));

        // Assert
        Assert.Single(message.Segments);
        Assert.Equal("first", Assert.IsType<TextContent>(message.Segments[0]).Content);
    }

    [Fact]
    public void Message_FromText_CreatesTextMessage()
    {
        // Arrange

        // Act
        var message = ContextMessage.FromText(MessageRole.User, "hello");

        // Assert
        Assert.Equal(MessageRole.User, message.Role);
        Assert.Single(message.Segments);
        Assert.Equal("hello", Assert.IsType<TextContent>(message.Segments[0]).Content);
        Assert.Equal(CompactionState.Original, message.State);
        Assert.Null(message.TokenCount);
    }

    [Fact]
    public void Message_FromText_RejectsNullOrWhitespace()
    {
        // Arrange

        // Act
        Action actOnNull = () => ContextMessage.FromText(MessageRole.User, null!);
        Action actOnEmpty = () => ContextMessage.FromText(MessageRole.User, string.Empty);
        Action actOnWhitespace = () => ContextMessage.FromText(MessageRole.User, "   ");

        // Assert
        Assert.Throws<ArgumentException>(actOnNull);
        Assert.Throws<ArgumentException>(actOnEmpty);
        Assert.Throws<ArgumentException>(actOnWhitespace);
    }

    [Fact]
    public void Message_FromContent_CreatesSingleSegmentMessage()
    {
        // Arrange
        var segment = new ToolUseContent("call_1", "read_file", "{}");

        // Act
        var message = ContextMessage.FromContent(MessageRole.Model, segment);

        // Assert
        Assert.Equal(MessageRole.Model, message.Role);
        Assert.Single(message.Segments);
        Assert.Same(segment, message.Segments[0]);
        Assert.Equal(CompactionState.Original, message.State);
        Assert.Null(message.TokenCount);
    }

    [Fact]
    public void Message_WithModifiedState_PreservesOtherProperties()
    {
        // Arrange
        var original = new ContextMessage
        {
            Role = MessageRole.Model,
            Segments =
            [
                new TextContent("Thinking"),
                new ToolUseContent("call_1", "read_file", "{}"),
            ],
            Timestamp = DateTimeOffset.UtcNow.AddMinutes(-1),
            TokenCount = 42,
        };

        // Act
        var modified = original with { State = CompactionState.Masked };

        // Assert
        Assert.Equal(CompactionState.Masked, modified.State);
        Assert.Equal(original.Role, modified.Role);
        Assert.Equal(original.Segments, modified.Segments);
        Assert.Equal(original.Timestamp, modified.Timestamp);
        Assert.Equal(original.TokenCount, modified.TokenCount);
    }

    [Fact]
    public void Message_MixedContent_AssistantWithTextAndToolUse()
    {
        // Arrange

        // Act
        var message = new ContextMessage
        {
            Role = MessageRole.Model,
            Segments =
            [
                new TextContent("I will call a tool."),
                new ToolUseContent("call_1", "web_search", "{\"query\":\"token guard\"}"),
            ],
        };

        // Assert
        Assert.Equal(MessageRole.Model, message.Role);
        Assert.Equal(2, message.Segments.Count);
        Assert.IsType<TextContent>(message.Segments[0]);
        Assert.IsType<ToolUseContent>(message.Segments[1]);
    }
}
