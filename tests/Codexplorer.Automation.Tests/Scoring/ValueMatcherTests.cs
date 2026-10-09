using Codexplorer.Automation.Scoring;
using FluentAssertions;

namespace Codexplorer.Automation.Tests.Scoring;

/// <summary>Verifies endpoint classification and occurrence search.</summary>
public sealed class ValueMatcherTests
{
    /// <summary>Verifies boundary-sensitive literals, including later and overlapping occurrences.</summary>
    /// <param name="text">The cleaned text.</param>
    /// <param name="value">The literal.</param>
    /// <param name="expected">The expected verdict.</param>
    [Theory]
    [InlineData("PARSE", "Parse", true)]
    [InlineData("xParse", "Parse", false)]
    [InlineData("ParseConfig", "Parse", false)]
    [InlineData("130", "30", false)]
    [InlineData("300", "30", false)]
    [InlineData("300 then 30", "30", true)]
    [InlineData("30s", "30", true)]
    [InlineData("2Parse1", "Parse", true)]
    [InlineData("domain.go", "main.go", false)]
    [InlineData("v1.88", "1.88", true)]
    [InlineData("30.5", "30", true)]
    [InlineData("a/src/main.go:4", "src/main.go", true)]
    [InlineData("éParse", "Parse", false)]
    [InlineData("Parseé", "Parse", false)]
    [InlineData("٣30", "30", false)]
    [InlineData("30٣", "30", false)]
    [InlineData("..x", "..", true)]
    [InlineData("a-a-a", "a-a", true)]
    [InlineData("xa-a-a", "a-a", true)]
    [InlineData("xa..a..", "a..", true)]
    public void Match_ExaminesEveryOccurrence(string text, string value, bool expected) =>
        ValueMatcher.IsMatch(text, value).Should().Be(expected);
}
