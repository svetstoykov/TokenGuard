using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;

namespace TokenGuard.Tests.Models;

public sealed class PrepareResultTests
{
    [Fact]
    public void Constructor_WhenMessagesIsNull_ThrowsArgumentNullException()
    {
        // Arrange

        // Act
        Action act = () => _ = new PrepareResult(null!, PrepareOutcome.Ready, 10, 10, 0);

        // Assert
        Assert.Equal("messages", Assert.Throws<ArgumentNullException>(act).ParamName);
    }
}
