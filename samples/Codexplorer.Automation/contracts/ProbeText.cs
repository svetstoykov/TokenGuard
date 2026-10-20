using System.Text;

namespace Codexplorer.Measurements;

/// <summary>Provides shared normalization and literal matching for probe measurements and scoring.</summary>
internal static class ProbeText
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

    /// <summary>Tries all occurrences, including overlaps, against endpoint boundaries.</summary>
    /// <param name="normalizedText">The cleaned source text.</param>
    /// <param name="normalizedValue">The nonempty cleaned literal.</param>
    /// <returns>Whether an occurrence satisfies both boundaries.</returns>
    public static bool IsMatch(string normalizedText, string normalizedValue)
    {
        if (normalizedValue.Length == 0)
            return false;
        for (var start = 0; start <= normalizedText.Length - normalizedValue.Length;)
        {
            var index = normalizedText.IndexOf(normalizedValue, start, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return false;
            var end = index + normalizedValue.Length;
            if ((index == 0 || !SplitsRun(normalizedValue[0], normalizedText[index - 1]))
                && (end == normalizedText.Length || !SplitsRun(normalizedValue[^1], normalizedText[end])))
                return true;
            start = index + 1;
        }
        return false;
    }

    private static bool SplitsRun(char endpoint, char adjacent) =>
        (char.IsLetter(endpoint) && char.IsLetter(adjacent)) || (char.IsDigit(endpoint) && char.IsDigit(adjacent));
}
