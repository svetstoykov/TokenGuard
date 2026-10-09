using System.Text;

namespace Codexplorer.Automation.Scoring;

/// <summary>Provides the literal scoring text cleaning rules.</summary>
internal static class TextNormalizer
{
    /// <summary>Removes Markdown markers, normalizes slashes and collapses whitespace in that order.</summary>
    /// <param name="text">The non-null source text.</param>
    /// <returns>The cleaned text.</returns>
    public static string Normalize(string text)
    {
        var result = new StringBuilder(text.Length);
        var space = false;
        foreach (var original in text)
        {
            if (original is '`' or '*')
                continue;
            var character = original == '\\' ? '/' : original;
            if (char.IsWhiteSpace(character))
            {
                space = result.Length > 0;
                continue;
            }
            if (space)
                result.Append(' ');
            result.Append(character);
            space = false;
        }
        return result.ToString();
    }
}
