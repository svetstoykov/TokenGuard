using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;
using TokenGuard.Core.Enums;

namespace TokenGuard.Tests.Models;

public sealed class CompactionResultTests
{
    [Fact]
    public void Constructor_DefaultsSummarizationErrorToNull()
    {
        // Arrange
        var messages = new List<ContextMessage> { ContextMessage.FromText(MessageRole.User, "hi") };

        // Act
        var result = new CompactionResult(messages, 10, 5, 1, "TestStrategy");

        // Assert
        Assert.Null(result.SummarizationError);
    }

    [Fact]
    public void Constructor_PreservesSuppliedSummarizationError()
    {
        // Arrange
        var messages = new List<ContextMessage> { ContextMessage.FromText(MessageRole.User, "hi") };
        var error = new InvalidOperationException("boom");

        // Act
        var result = new CompactionResult(messages, 10, 5, 1, "TestStrategy", error);

        // Assert
        Assert.Same(error, result.SummarizationError);
    }
}
