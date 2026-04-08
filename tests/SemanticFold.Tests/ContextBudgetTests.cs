namespace SemanticFold.Tests;

public sealed class ContextBudgetTests
{
    [Fact]
    public void For_UsesDefaultValues()
    {
        var budget = ContextBudget.For(100_000);

        Assert.Equal(100_000, budget.MaxTokens);
        Assert.Equal(0.80, budget.CompactionThreshold);
        Assert.Equal(0.95, budget.EmergencyThreshold);
        Assert.Equal(0, budget.ReservedTokens);
        Assert.Equal(100_000, budget.AvailableTokens);
        Assert.Equal(80_000, budget.CompactionTriggerTokens);
        Assert.Equal(95_000, budget.EmergencyTriggerTokens);
    }

    [Fact]
    public void AvailableTokens_SubtractsReservedTokens()
    {
        var budget = new ContextBudget(maxTokens: 10_000, reservedTokens: 1_250);

        Assert.Equal(8_750, budget.AvailableTokens);
    }

    [Fact]
    public void TriggerTokens_UseAvailableTokens_NotMaxTokens()
    {
        var budget = new ContextBudget(
            maxTokens: 1_000,
            compactionThreshold: 0.50,
            emergencyThreshold: 0.75,
            reservedTokens: 200);

        Assert.Equal(800, budget.AvailableTokens);
        Assert.Equal(400, budget.CompactionTriggerTokens);
        Assert.Equal(600, budget.EmergencyTriggerTokens);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_ThrowsForInvalidMaxTokens(int maxTokens)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _ = new ContextBudget(maxTokens));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100)]
    [InlineData(101)]
    public void Constructor_ThrowsForInvalidReservedTokens(int reservedTokens)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _ = new ContextBudget(maxTokens: 100, reservedTokens: reservedTokens));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void Constructor_ThrowsForInvalidCompactionThreshold(double compactionThreshold)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _ = new ContextBudget(maxTokens: 100, compactionThreshold: compactionThreshold));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void Constructor_ThrowsForInvalidEmergencyThreshold(double emergencyThreshold)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _ = new ContextBudget(maxTokens: 100, emergencyThreshold: emergencyThreshold));
    }

    [Fact]
    public void Constructor_ThrowsWhenCompactionThresholdIsGreaterThanOrEqualToEmergencyThreshold()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _ = new ContextBudget(maxTokens: 100, compactionThreshold: 0.80, emergencyThreshold: 0.80));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _ = new ContextBudget(maxTokens: 100, compactionThreshold: 0.90, emergencyThreshold: 0.80));
    }
}
