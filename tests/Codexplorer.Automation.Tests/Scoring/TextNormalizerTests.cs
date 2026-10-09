using Codexplorer.Automation.Scoring;
using FluentAssertions;

namespace Codexplorer.Automation.Tests.Scoring;

/// <summary>Verifies the exact ordered cleaning rules.</summary>
public sealed class TextNormalizerTests
{
    /// <summary>Verifies markers, slashes and all whitespace without broadening punctuation cleaning.</summary>
    /// <param name="text">The source.</param>
    /// <param name="expected">The cleaned result.</param>
    [Theory]
    [InlineData(" **mul`ti word`** ", "multi word")]
    [InlineData("a\\.github\\b", "a/.github/b")]
    [InlineData(" a\t\n\u00a0b ", "a b")]
    [InlineData("a * ` \t b", "a b")]
    [InlineData("_‘a’—b_", "_‘a’—b_")]
    public void Normalize_UsesOnlySpecifiedRules(string text, string expected) => TextNormalizer.Normalize(text).Should().Be(expected);
}
