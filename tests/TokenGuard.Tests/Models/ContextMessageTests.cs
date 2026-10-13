using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;

namespace TokenGuard.Tests.Models;

public sealed class ContextMessageTests
{
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
    public void Message_FromContent_WhenSegmentIsNull_ThrowsArgumentNullException()
    {
        // Arrange

        // Act
        Action act = () => ContextMessage.FromContent(MessageRole.Model, (ContentSegment)null!);

        // Assert
        Assert.Throws<ArgumentNullException>(act);
    }

    [Fact]
    public void Message_FromContent_WithSegmentArray_CreatesMessageWithEverySegmentInOrder()
    {
        // Arrange
        ContentSegment[] segments = [new TextContent("I will call a tool."), new ToolUseContent("call_1", "web_search", "{\"query\":\"tokens\"}")];

        // Act
        var message = ContextMessage.FromContent(MessageRole.Model, segments);

        // Assert
        Assert.Equal(MessageRole.Model, message.Role);
        Assert.Equal(segments, message.Segments);
        Assert.Equal(CompactionState.Original, message.State);
        Assert.False(message.IsPinned);
        Assert.Null(message.TokenCount);
    }

    [Fact]
    public void Message_FromContent_WithSegmentArray_DoesNotFollowLaterChangesToTheArray()
    {
        // Arrange
        ContentSegment[] segments = [new TextContent("first"), new TextContent("second")];
        var message = ContextMessage.FromContent(MessageRole.User, segments);

        // Act
        segments[0] = new TextContent("replaced");

        // Assert
        Assert.Equal("first", Assert.IsType<TextContent>(message.Segments[0]).Content);
    }

    [Fact]
    public void Message_FromContent_WhenSegmentArrayIsNull_ThrowsArgumentNullException()
    {
        // Arrange

        // Act
        Action act = () => ContextMessage.FromContent(MessageRole.Model, (ContentSegment[])null!);

        // Assert
        Assert.Throws<ArgumentNullException>(act);
    }

    [Fact]
    public void Message_FromContent_WhenSegmentArrayIsEmpty_ThrowsArgumentException()
    {
        // Arrange

        // Act
        Action act = () => ContextMessage.FromContent(MessageRole.Model, Array.Empty<ContentSegment>());

        // Assert
        Assert.Throws<ArgumentException>(act);
    }
}
