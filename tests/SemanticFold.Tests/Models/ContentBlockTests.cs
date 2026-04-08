using SemanticFold.Models;
using SemanticFold.Models.Content;

namespace SemanticFold.Tests.Models;

public sealed class ContentBlockTests
{
    [Fact]
    public void TextContent_Stores_Text()
    {
        var content = new TextContent("hello");

        Assert.Equal("hello", content.Text);
    }

    [Fact]
    public void TextContent_RejectsNullOrWhitespace()
    {
        Assert.Throws<ArgumentException>(() => new TextContent(null!));
        Assert.Throws<ArgumentException>(() => new TextContent(string.Empty));
        Assert.Throws<ArgumentException>(() => new TextContent("   "));
    }

    [Fact]
    public void ToolUseContent_Stores_Properties()
    {
        var content = new ToolUseContent("call_1", "read_file", "{\"path\":\"a.txt\"}");

        Assert.Equal("call_1", content.ToolCallId);
        Assert.Equal("read_file", content.ToolName);
        Assert.Equal("{\"path\":\"a.txt\"}", content.ArgumentsJson);
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
        Assert.Throws<ArgumentException>(() => new ToolUseContent(toolCallId!, toolName!, argumentsJson!));
    }

    [Fact]
    public void ToolResultContent_Stores_Properties()
    {
        var content = new ToolResultContent("call_1", "read_file", "file contents");

        Assert.Equal("call_1", content.ToolCallId);
        Assert.Equal("read_file", content.ToolName);
        Assert.Equal("file contents", content.Content);
    }

    [Fact]
    public void ToolResultContent_AllowsEmptyContent()
    {
        var exception = Record.Exception(() => new ToolResultContent("call_1", "read_file", string.Empty));

        Assert.Null(exception);
    }

    [Fact]
    public void ToolResultContent_RejectsNullContent()
    {
        Assert.Throws<ArgumentNullException>(() => new ToolResultContent("call_1", "read_file", null!));
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
        Assert.Throws<ArgumentException>(() => new ToolResultContent(toolCallId!, toolName!, "payload"));
    }

    [Fact]
    public void ContentBlock_PatternMatching()
    {
        static string Describe(ContentBlock block)
        {
            return block switch
            {
                TextContent => "text",
                ToolUseContent => "tool_use",
                ToolResultContent => "tool_result",
                _ => "unknown",
            };
        }

        Assert.Equal("text", Describe(new TextContent("hello")));
        Assert.Equal("tool_use", Describe(new ToolUseContent("call_1", "run", "{}")));
        Assert.Equal("tool_result", Describe(new ToolResultContent("call_1", "run", "ok")));
    }
}
