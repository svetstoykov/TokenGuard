using SemanticFold.Enums;
using SemanticFold.Models;
using SemanticFold.Models.Content;
using SemanticFold.TokenCounting;

namespace SemanticFold.Tests.TokenCounting;

public class EstimatedTokenCounterTests
{
    private readonly EstimatedTokenCounter counter = new();

    [Fact]
    public void EmptyMessageList_ReturnsZero()
    {
        // Arrange
        var messages = Array.Empty<Message>();

        // Act
        int result = this.counter.Count(messages);

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public void SingleTextMessage_ReturnsCeilingEstimatePlusOverhead()
    {
        // Arrange
        // "Hello" is 5 chars. 5/4 = 1.25. Ceiling is 2. Overhead is 4. Total = 6.
        var message = Message.FromText(MessageRole.User, "Hello");

        // Act
        int result = this.counter.Count(message);

        // Assert
        Assert.Equal(6, result);
    }

    [Fact]
    public void MultiBlockMessage_AccumulatesCharacterCounts()
    {
        // Arrange
        // Text: "Hello" (5 chars)
        // ToolUse: name "calc" (4 chars), input "{}" (2 chars)
        // Total chars: 5 + 4 + 2 = 11.
        // 11/4 = 2.75. Ceiling is 3. Overhead is 4. Total = 7.
        var message = new Message
        {
            Role = MessageRole.Model,
            Content = [
                new TextContent("Hello"),
                new ToolUseContent("call_1", "calc", "{}")
            ]
        };

        // Act
        int result = this.counter.Count(message);

        // Assert
        Assert.Equal(7, result);
    }

    [Fact]
    public void CountEnumerable_EqualsSumOfIndividualCalls()
    {
        // Arrange
        var msg1 = Message.FromText(MessageRole.User, "One"); // 3 chars -> 1 token + 4 overhead = 5
        var msg2 = Message.FromText(MessageRole.Model, "Two"); // 3 chars -> 1 token + 4 overhead = 5
        var messages = new[] { msg1, msg2 };

        // Act
        int totalResult = this.counter.Count(messages);
        int individualSum = this.counter.Count(msg1) + this.counter.Count(msg2);

        // Assert
        Assert.Equal(10, totalResult);
        Assert.Equal(individualSum, totalResult);
    }

    [Fact]
    public void MessageWithTokenCountSet_ReturnsCachedValue()
    {
        // Arrange
        var message = new Message
        {
            Role = MessageRole.User,
            Content = [new TextContent("Some text")],
            TokenCount = 100 // Deliberately different from estimate
        };

        // Act
        int result = this.counter.Count(message);

        // Assert
        Assert.Equal(100, result);
    }

    [Fact]
    public void ToolResultContent_CountsCorrectly()
    {
        // Arrange
        // ToolResult: ToolCallId "call_1" (6 chars), Content "42" (2 chars)
        // Total chars: 8. 8/4 = 2. Overhead is 4. Total = 6.
        var message = Message.FromContent(MessageRole.User, new ToolResultContent("call_1", "calc", "42"));

        // Act
        int result = this.counter.Count(message);

        // Assert
        Assert.Equal(6, result);
    }
}
