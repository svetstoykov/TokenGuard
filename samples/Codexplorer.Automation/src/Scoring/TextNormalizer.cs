namespace Codexplorer.Automation.Scoring;

/// <summary>Provides the literal scoring text cleaning rules.</summary>
internal static class TextNormalizer
{
    /// <summary>Removes Markdown markers, normalizes slashes and collapses whitespace in that order.</summary>
    /// <param name="text">The non-null source text.</param>
    /// <returns>The cleaned text.</returns>
    public static string Normalize(string text) =>
        Codexplorer.Measurements.ProbeText.Normalize(text);
}
