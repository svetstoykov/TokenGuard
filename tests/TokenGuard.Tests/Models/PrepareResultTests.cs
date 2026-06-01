using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;

namespace TokenGuard.Tests.Models;

public sealed class PrepareResultTests
{
    [Fact]
    public void Constructor_DefaultsSummarizationErrorToNull()
    {
        // Arrange
        var messages = new List<ContextMessage> { ContextMessage.FromText(MessageRole.User, "hi") };

        // Act
        var result = new PrepareResult(messages, PrepareOutcome.Ready, 10, 10, 0);

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
        var result = new PrepareResult(messages, PrepareOutcome.Compacted, 100, 40, 2, null, 0, error);

        // Assert
        Assert.Same(error, result.SummarizationError);
    }
}
