using TokenGuard.Core.Models;

namespace TokenGuard.Tests.TokenCounting;

public sealed class ContextBudgetTests
{
    [Fact]
    public void For_UsesDefaultValues()
    {
        // Arrange

        // Act
        var budget = ContextBudget.For(100_000);

        // Assert
        Assert.Equal(100_000, budget.MaxTokens);
        Assert.Equal(0.80, budget.CompactionThreshold);
        Assert.Null(budget.EmergencyThreshold);
        Assert.Equal(0, budget.ReservedTokens);
        Assert.Equal(0.05, budget.OverrunTolerance);
        Assert.Equal(5_000, budget.OverrunToleranceTokens);
        Assert.Equal(100_000, budget.AvailableTokens);
        Assert.Equal(80_000, budget.CompactionTriggerTokens);
        Assert.Null(budget.EmergencyTriggerTokens);
    }

    [Fact]
    public void Constructor_WhenEmergencyThresholdIsNull_EmergencyTriggerTokensIsNull()
    {
        // Arrange

        // Act
        var budget = new ContextBudget(maxTokens: 1_000, compactionThreshold: 0.80);

        // Assert
        Assert.Null(budget.EmergencyThreshold);
        Assert.Null(budget.EmergencyTriggerTokens);
    }

    [Fact]
    public void AvailableTokens_SubtractsReservedTokens()
    {
        // Arrange

        // Act
        var budget = new ContextBudget(maxTokens: 10_000, reservedTokens: 1_250);

        // Assert
        Assert.Equal(8_750, budget.AvailableTokens);
    }

    [Fact]
    public void TriggerTokens_UseAvailableTokens_NotMaxTokens()
    {
        // Arrange

        // Act
        var budget = new ContextBudget(
            maxTokens: 1_000,
            compactionThreshold: 0.50,
            emergencyThreshold: 0.75,
            reservedTokens: 200);

        // Assert
        Assert.Equal(800, budget.AvailableTokens);
        Assert.Equal(400, budget.CompactionTriggerTokens);
        Assert.Equal(600, budget.EmergencyTriggerTokens!.Value);
    }

    [Fact]
    public void CompactionTriggerTokens_ComputesExactBoundaryValue()
    {
        // Arrange
        var budget = new ContextBudget(
            maxTokens: 1_000,
            compactionThreshold: 0.30,
            emergencyThreshold: 0.60,
            reservedTokens: 0);

        // Act
        var exactBoundary = budget.CompactionTriggerTokens;
        var oneAboveBoundary = budget.CompactionTriggerTokens + 1;

        // Assert
        Assert.Equal(300, exactBoundary);
        Assert.Equal(301, oneAboveBoundary);
        Assert.True(exactBoundary <= budget.AvailableTokens);
        Assert.True(oneAboveBoundary <= budget.AvailableTokens);
    }

    [Fact]
    public void EmergencyTriggerTokens_ComputesExactBoundaryValue()
    {
        // Arrange
        var budget = new ContextBudget(
            maxTokens: 1_000,
            compactionThreshold: 0.30,
            emergencyThreshold: 0.95,
            reservedTokens: 0);

        // Act
        var exactBoundary = budget.EmergencyTriggerTokens!.Value;
        var oneAboveBoundary = budget.EmergencyTriggerTokens.Value + 1;

        // Assert
        Assert.Equal(950, exactBoundary);
        Assert.Equal(951, oneAboveBoundary);
        Assert.True(exactBoundary <= budget.AvailableTokens);
        Assert.True(oneAboveBoundary <= budget.AvailableTokens);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_ThrowsForInvalidMaxTokens(int maxTokens)
    {
        // Arrange

        // Act
        Action act = () => _ = new ContextBudget(maxTokens);

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(act);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100)]
    [InlineData(101)]
    public void Constructor_ThrowsForInvalidReservedTokens(int reservedTokens)
    {
        // Arrange

        // Act
        Action act = () => _ = new ContextBudget(maxTokens: 100, reservedTokens: reservedTokens);

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(act);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void Constructor_ThrowsForInvalidCompactionThreshold(double compactionThreshold)
    {
        // Arrange

        // Act
        Action act = () => _ = new ContextBudget(maxTokens: 100, compactionThreshold: compactionThreshold);

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(act);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void Constructor_ThrowsForInvalidEmergencyThreshold(double emergencyThreshold)
    {
        // Arrange

        // Act
        Action act = () => _ = new ContextBudget(maxTokens: 100, emergencyThreshold: emergencyThreshold);

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(act);
    }

    [Fact]
    public void Constructor_ThrowsWhenCompactionThresholdIsGreaterThanOrEqualToEmergencyThreshold()
    {
        // Arrange

        // Act
        Action actOnEqualThresholds = () => _ = new ContextBudget(maxTokens: 100, compactionThreshold: 0.80, emergencyThreshold: 0.80);
        Action actOnInvertedThresholds = () => _ = new ContextBudget(maxTokens: 100, compactionThreshold: 0.90, emergencyThreshold: 0.80);

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(actOnEqualThresholds);
        Assert.Throws<ArgumentOutOfRangeException>(actOnInvertedThresholds);
    }

    [Fact]
    public void OverrunTolerance_DefaultsToFivePercent()
    {
        // Arrange

        // Act
        var budget = new ContextBudget(maxTokens: 1_000);

        // Assert
        Assert.Equal(0.05, budget.OverrunTolerance);
        Assert.Equal(50, budget.OverrunToleranceTokens);
    }

    [Fact]
    public void OverrunTolerance_StoresConfiguredValue()
    {
        // Arrange

        // Act
        var budget = new ContextBudget(maxTokens: 1_000, overrunTolerance: 0.10);

        // Assert
        Assert.Equal(0.10, budget.OverrunTolerance);
        Assert.Equal(100, budget.OverrunToleranceTokens);
    }

    [Fact]
    public void OverrunToleranceTokens_FloorsPartialTokens()
    {
        // Arrange — 5% of 999 = 49.95, which must floor to 49, not round to 50.

        // Act
        var budget = new ContextBudget(maxTokens: 999, overrunTolerance: 0.05);

        // Assert
        Assert.Equal(49, budget.OverrunToleranceTokens);
    }

    [Fact]
    public void Constructor_ThrowsForNegativeOverrunTolerance()
    {
        // Arrange

        // Act
        Action act = () => _ = new ContextBudget(maxTokens: 1_000, overrunTolerance: -0.01);

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(act);
    }

    [Fact]
    public void Constructor_ThrowsWhenOverrunToleranceExceedsOne()
    {
        // Arrange

        // Act
        Action act = () => _ = new ContextBudget(maxTokens: 1_000, overrunTolerance: 1.01);

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(act);
    }

    [Fact]
    public void Constructor_ThrowsForNonFiniteOverrunTolerance()
    {
        // Arrange

        // Act
        Action actNaN = () => _ = new ContextBudget(maxTokens: 1_000, overrunTolerance: double.NaN);
        Action actInf = () => _ = new ContextBudget(maxTokens: 1_000, overrunTolerance: double.PositiveInfinity);

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(actNaN);
        Assert.Throws<ArgumentOutOfRangeException>(actInf);
    }
}
