using FluentAssertions;
using TokenGuard.Core.Models;
using TokenGuard.Tests.Diagnostics;

namespace TokenGuard.Tests.Models;

public sealed class ConversationContextConfigurationTests
{
    [Theory]
    [InlineData(40_000, 10_000)]
    [InlineData(200_000, 25_000)]
    public void Constructor_UsesTheDefaultToolResultLimitForTheBudget(int maxTokens, int expected)
    {
        // Arrange
        var budget = ContextBudget.For(maxTokens);

        // Act
        var configuration = new ConversationContextConfiguration(budget, _ => StubCompactionStrategy.Unchanged());

        // Assert
        configuration.MaxToolResultTokens.Should().Be(expected);
    }

    [Fact]
    public void With_WhenOnlyTheBudgetChanges_KeepsTheEarlierToolResultLimit()
    {
        // Arrange
        var configuration = new ConversationContextConfiguration(ContextBudget.For(40_000), _ => StubCompactionStrategy.Unchanged());

        // Act
        var changed = configuration with { Budget = ContextBudget.For(200_000) };

        // Assert
        changed.MaxToolResultTokens.Should().Be(10_000);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void MaxToolResultTokens_WhenSetToANonPositiveValue_ThrowsArgumentOutOfRangeException(int maxToolResultTokens)
    {
        // Arrange
        var configuration = new ConversationContextConfiguration(ContextBudget.For(40_000), _ => StubCompactionStrategy.Unchanged());

        // Act
        Action act = () => _ = configuration with { MaxToolResultTokens = maxToolResultTokens };

        // Assert
        act.Should().ThrowExactly<ArgumentOutOfRangeException>().WithParameterName("value");
    }
}
