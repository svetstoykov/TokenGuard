using TokenGuard.Core.Options;

namespace TokenGuard.Tests.Options;

public sealed class SlidingWindowOptionsTests
{
    [Fact]
    public void ExemptToolNames_OnDefaultOptions_IsEmpty()
    {
        // Arrange

        // Act
        var names = SlidingWindowOptions.Default.ExemptToolNames;

        // Assert
        Assert.Empty(names);
    }

    [Fact]
    public void ExemptToolNames_AfterParameterlessConstructor_IsEmpty()
    {
        // Arrange

        // Act
        var names = new SlidingWindowOptions().ExemptToolNames;

        // Assert
        Assert.Empty(names);
    }

    [Fact]
    public void ExemptToolNames_OnDefaultValue_IsEmpty()
    {
        // Arrange

        // Act
        var names = default(SlidingWindowOptions).ExemptToolNames;

        // Assert
        Assert.Empty(names);
    }

    [Fact]
    public void ExemptToolNames_WhenNamesRepeat_HoldsEachNameOnce()
    {
        // Arrange

        // Act
        var options = new SlidingWindowOptions(windowSize: 2) { ExemptToolNames = ["todo_write", "notes", "todo_write"] };

        // Assert
        Assert.Equal(["notes", "todo_write"], options.ExemptToolNames.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ExemptToolNames_WhenNamesDifferOnlyInCase_HoldsBothNames()
    {
        // Arrange

        // Act
        var options = new SlidingWindowOptions { ExemptToolNames = ["todo_write", "Todo_Write"] };

        // Assert
        Assert.Equal(2, options.ExemptToolNames.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ExemptToolNames_WhenAnEntryIsNullOrWhitespace_Throws(string? name)
    {
        // Arrange
        string[] names = ["todo_write", name!];

        // Act
        Action act = () => _ = new SlidingWindowOptions { ExemptToolNames = names };

        // Assert
        var exception = Assert.Throws<ArgumentException>(act);
        Assert.Equal("ExemptToolNames", exception.ParamName);
    }

    [Fact]
    public void ExemptToolNames_WhenCollectionIsNull_Throws()
    {
        // Arrange

        // Act
        Action act = () => _ = new SlidingWindowOptions { ExemptToolNames = null! };

        // Assert
        var exception = Assert.Throws<ArgumentNullException>(act);
        Assert.Equal("ExemptToolNames", exception.ParamName);
    }

    [Fact]
    public void ExemptToolNames_WhenSourceListChangesAfterInit_KeepsTheNamesGiven()
    {
        // Arrange
        var source = new List<string> { "todo_write" };
        var options = new SlidingWindowOptions { ExemptToolNames = source };

        // Act
        source.Add("notes");

        // Assert
        Assert.Equal(["todo_write"], options.ExemptToolNames);
    }

    [Fact]
    public void With_WhenExemptToolNamesAreSet_KeepsTheOtherValues()
    {
        // Arrange
        var options = new SlidingWindowOptions(windowSize: 3, protectedWindowFraction: 0.40, placeholderFormat: "[cleared {0} {1}]");

        // Act
        var changed = options with { ExemptToolNames = ["notes"] };

        // Assert
        Assert.Equal(3, changed.WindowSize);
        Assert.Equal(0.40, changed.ProtectedWindowFraction);
        Assert.Equal("[cleared {0} {1}]", changed.PlaceholderFormat);
        Assert.Equal(["notes"], changed.ExemptToolNames);
    }

    [Fact]
    public void Equals_WhenSameNamesAreGivenInAnotherOrderOrRepeated_ReturnsTrueWithEqualHashCodes()
    {
        // Arrange
        var first = new SlidingWindowOptions(windowSize: 2) { ExemptToolNames = ["notes", "todo_write"] };
        var second = new SlidingWindowOptions(windowSize: 2) { ExemptToolNames = ["todo_write", "notes", "notes"] };

        // Act
        var equal = first.Equals(second);

        // Assert
        Assert.True(equal);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Equals_WhenExplicitlyEmptyAndUnset_ReturnsTrueWithEqualHashCodes()
    {
        // Arrange
        var unset = new SlidingWindowOptions(windowSize: 2);
        var empty = new SlidingWindowOptions(windowSize: 2) { ExemptToolNames = [] };

        // Act
        var equal = unset == empty;

        // Assert
        Assert.True(equal);
        Assert.Equal(unset.GetHashCode(), empty.GetHashCode());
    }

    [Fact]
    public void Equals_WhenNamesDiffer_ReturnsFalse()
    {
        // Arrange
        var first = new SlidingWindowOptions { ExemptToolNames = ["todo_write"] };
        var second = new SlidingWindowOptions { ExemptToolNames = ["Todo_Write"] };

        // Act
        var equal = first == second;

        // Assert
        Assert.False(equal);
    }

    [Theory]
    [InlineData(3, 0.80, "[{0} {1}]")]
    [InlineData(2, 0.50, "[{0} {1}]")]
    [InlineData(2, 0.80, "[{1} {0}]")]
    public void Equals_WhenAConstructorValueDiffers_ReturnsFalse(int windowSize, double protectedWindowFraction, string placeholderFormat)
    {
        // Arrange
        var first = new SlidingWindowOptions(2, 0.80, "[{0} {1}]") { ExemptToolNames = ["notes"] };
        var second = new SlidingWindowOptions(windowSize, protectedWindowFraction, placeholderFormat) { ExemptToolNames = ["notes"] };

        // Act
        var equal = first == second;

        // Assert
        Assert.False(equal);
    }

    [Fact]
    public void Equals_WhenBothAreDefaultValues_ReturnsTrue()
    {
        // Arrange
        var first = default(SlidingWindowOptions);
        var second = default(SlidingWindowOptions);

        // Act
        var equal = first.Equals(second);

        // Assert
        Assert.True(equal);
    }
}
