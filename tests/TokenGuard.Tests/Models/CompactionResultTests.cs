using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;
using TokenGuard.Core.Enums;

namespace TokenGuard.Tests.Models;

public sealed class CompactionResultTests
{
    [Fact]
    public void Constructor_WhenMessagesIsNull_ThrowsArgumentNullException()
    {
        // Arrange

        // Act
        Action act = () => _ = new CompactionResult(null!, 10, 5, 1, "TestStrategy");

        // Assert
        Assert.Equal("messages", Assert.Throws<ArgumentNullException>(act).ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WhenStrategyNameIsNullOrWhitespace_ThrowsArgumentException(string? strategyName)
    {
        // Arrange
        var messages = new List<ContextMessage> { ContextMessage.FromText(MessageRole.User, "hi") };

        // Act
        Action act = () => _ = new CompactionResult(messages, 10, 5, 1, strategyName!);

        // Assert
        Assert.Equal("strategyName", Assert.ThrowsAny<ArgumentException>(act).ParamName);
    }
}
