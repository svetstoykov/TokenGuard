using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;

namespace TokenGuard.Tests.Models;

public sealed class ContentSegmentTests
{
    [Fact]
    public void TextContent_Stores_Text()
    {
        // Arrange

        // Act
        var content = new TextContent("hello");

        // Assert
        Assert.Equal("hello", content.Content);
    }

    [Fact]
    public void TextContent_RejectsNullOrWhitespace()
    {
        // Arrange

        // Act
        Action actOnNull = () => new TextContent(null!);
        Action actOnEmpty = () => new TextContent(string.Empty);
        Action actOnWhitespace = () => new TextContent("   ");

        // Assert
        Assert.Throws<ArgumentException>(actOnNull);
        Assert.Throws<ArgumentException>(actOnEmpty);
        Assert.Throws<ArgumentException>(actOnWhitespace);
    }

    [Fact]
    public void ToolUseContent_Stores_Properties()
    {
        // Arrange

        // Act
        var content = new ToolUseContent("call_1", "read_file", "{\"path\":\"a.txt\"}");

        // Assert
        Assert.Equal("call_1", content.ToolCallId);
        Assert.Equal("read_file", content.ToolName);
        Assert.Equal("{\"path\":\"a.txt\"}", content.Content);
    }

    [Theory]
    [InlineData(null, "tool", "{}")]
    [InlineData("", "tool", "{}")]
    [InlineData("   ", "tool", "{}")]
    [InlineData("id", null, "{}")]
    [InlineData("id", "", "{}")]
    [InlineData("id", "   ", "{}")]
    [InlineData("id", "tool", null)]
    [InlineData("id", "tool", "")]
    [InlineData("id", "tool", "   ")]
    public void ToolUseContent_RejectsNullOrWhitespace(string? toolCallId, string? toolName, string? argumentsJson)
    {
        // Arrange

        // Act
        Action act = () => new ToolUseContent(toolCallId!, toolName!, argumentsJson!);

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void ToolResultContent_Stores_Properties()
    {
        // Arrange

        // Act
        var content = new ToolResultContent("call_1", "read_file", "file contents");

        // Assert
        Assert.Equal("call_1", content.ToolCallId);
        Assert.Equal("read_file", content.ToolName);
        Assert.Equal("file contents", content.Content);
    }

    [Fact]
    public void ToolResultContent_AllowsEmptyContent()
    {
        // Arrange

        // Act
        var exception = Record.Exception(() => new ToolResultContent("call_1", "read_file", string.Empty));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void ToolResultContent_RejectsNullContent()
    {
        // Arrange

        // Act
        Action act = () => new ToolResultContent("call_1", "read_file", null!);

        // Assert
        Assert.Throws<ArgumentNullException>(act);
    }

    [Theory]
    [InlineData(null, "tool")]
    [InlineData("", "tool")]
    [InlineData("   ", "tool")]
    [InlineData("id", null)]
    [InlineData("id", "")]
    [InlineData("id", "   ")]
    public void ToolResultContent_RejectsNullOrWhitespaceIdAndName(string? toolCallId, string? toolName)
    {
        // Arrange

        // Act
        Action act = () => new ToolResultContent(toolCallId!, toolName!, "payload");

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void ContentSegment_PatternMatching()
    {
        // Arrange
        static string Describe(ContentSegment segment)
        {
            return segment switch
            {
                TextContent => "text",
                ToolUseContent => "tool_use",
                ToolResultContent => "tool_result",
                _ => "unknown",
            };
        }

        // Act
        var textDescription = Describe(new TextContent("hello"));
        var toolUseDescription = Describe(new ToolUseContent("call_1", "run", "{}"));
        var toolResultDescription = Describe(new ToolResultContent("call_1", "run", "ok"));

        // Assert
        Assert.Equal("text", textDescription);
        Assert.Equal("tool_use", toolUseDescription);
        Assert.Equal("tool_result", toolResultDescription);
    }
}
