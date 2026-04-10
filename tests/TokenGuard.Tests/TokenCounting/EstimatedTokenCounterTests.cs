using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;
using TokenGuard.Core.TokenCounting;

namespace TokenGuard.Tests.TokenCounting;

public class EstimatedTokenCounterTests
{
    private readonly EstimatedTokenCounter _counter = new();

    [Fact]
    public void EmptyMessageList_ReturnsZero()
    {
        // Arrange
        var messages = Array.Empty<SemanticMessage>();

        // Act
        var result = this._counter.Count(messages);

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public void SingleTextMessage_ReturnsCeilingEstimatePlusOverhead()
    {
        // Arrange
        var message = SemanticMessage.FromText(MessageRole.User, "Hello");

        // Act
        var result = this._counter.Count(message);

        // Assert
        Assert.Equal(6, result);
    }

    [Fact]
    public void MultiBlockMessage_AccumulatesCharacterCounts()
    {
        // Arrange
        var message = new SemanticMessage
        {
            Role = MessageRole.Model,
            Content = [
                new TextContent("Hello"),
                new ToolUseContent("call_1", "calc", "{}")
            ]
        };

        // Act
        var result = this._counter.Count(message);

        // Assert
        Assert.Equal(7, result);
    }

    [Fact]
    public void CountEnumerable_EqualsSumOfIndividualCalls()
    {
        // Arrange
        var msg1 = SemanticMessage.FromText(MessageRole.User, "One");
        var msg2 = SemanticMessage.FromText(MessageRole.Model, "Two");
        var messages = new[] { msg1, msg2 };

        // Act
        var totalResult = this._counter.Count(messages);
        var individualSum = this._counter.Count(msg1) + this._counter.Count(msg2);

        // Assert
        Assert.Equal(10, totalResult);
        Assert.Equal(individualSum, totalResult);
    }

    [Fact]
    public void MessageWithTokenCountSet_ReturnsCachedValue()
    {
        // Arrange
        var message = new SemanticMessage
        {
            Role = MessageRole.User,
            Content = [new TextContent("Some text")],
            TokenCount = 100,
        };

        // Act
        var result = this._counter.Count(message);

        // Assert
        Assert.Equal(100, result);
    }

    [Fact]
    public void ToolResultContent_CountsCorrectly()
    {
        // Arrange
        var message = SemanticMessage.FromContent(MessageRole.User, new ToolResultContent("call_1", "calc", "42"));

        // Act
        var result = this._counter.Count(message);

        // Assert
        Assert.Equal(6, result);
    }
}
