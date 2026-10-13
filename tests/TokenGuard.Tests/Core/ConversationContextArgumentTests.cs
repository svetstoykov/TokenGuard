using FluentAssertions;
using TokenGuard.Core;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Configuration;
using TokenGuard.Core.Models.Content;

namespace TokenGuard.Tests.Core;

public sealed class ConversationContextArgumentTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SetSystemPrompt_WhenTextIsNullOrWhitespace_ThrowsArgumentExceptionAndRecordsNothing(string? text)
    {
        // Arrange
        using var context = CreateContext();

        // Act
        Action act = () => context.SetSystemPrompt(text!);

        // Assert
        act.Should().Throw<ArgumentException>().WithParameterName("text");
        context.History.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddUserMessage_WhenTextIsNullOrWhitespace_ThrowsArgumentExceptionAndRecordsNothing(string? text)
    {
        // Arrange
        using var context = CreateContext();

        // Act
        Action act = () => context.AddUserMessage(text!);

        // Assert
        act.Should().Throw<ArgumentException>().WithParameterName("text");
        context.History.Should().BeEmpty();
    }

    [Fact]
    public void RecordModelResponse_WhenContentIsNull_ThrowsArgumentNullExceptionAndRecordsNothing()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        Action act = () => context.RecordModelResponse(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("content");
        context.History.Should().BeEmpty();
    }

    [Fact]
    public void RecordModelResponse_WhenContentIsEmpty_ThrowsArgumentExceptionAndRecordsNothing()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        Action act = () => context.RecordModelResponse(Array.Empty<ContentSegment>(), providerInputTokens: 10);

        // Assert
        act.Should().Throw<ArgumentException>().WithParameterName("content");
        context.History.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, "search", "toolCallId")]
    [InlineData("", "search", "toolCallId")]
    [InlineData("   ", "search", "toolCallId")]
    [InlineData("call_1", null, "toolName")]
    [InlineData("call_1", "", "toolName")]
    [InlineData("call_1", "   ", "toolName")]
    public void RecordToolResult_WhenToolCallIdOrToolNameIsNullOrWhitespace_ThrowsArgumentExceptionNamingIt(
        string? toolCallId, string? toolName, string parameterName)
    {
        // Arrange
        using var context = CreateContext();

        // Act
        Action act = () => context.RecordToolResult(toolCallId!, toolName!, "result");

        // Assert
        act.Should().Throw<ArgumentException>().WithParameterName(parameterName);
        context.History.Should().BeEmpty();
    }

    [Fact]
    public void RecordToolResult_WhenContentIsNull_ThrowsArgumentNullExceptionAndRecordsNothing()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        Action act = () => context.RecordToolResult("call_1", "search", null!);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("content");
        context.History.Should().BeEmpty();
    }

    [Fact]
    public void RecordToolResult_WhenContentIsEmpty_RecordsTheToolResult()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        context.RecordToolResult("call_1", "search", string.Empty);

        // Assert
        context.History.Should().ContainSingle().Which.Segments.Should().Equal(new ToolResultContent("call_1", "search", string.Empty));
    }

    private static IConversationContext CreateContext() => new ConversationContextFactory(ConversationConfigBuilder.Default()).Create();
}
