namespace Codexplorer.Automation.Scoring;

/// <summary>Provides ordinal literal matching with letter and digit boundaries.</summary>
internal static class ValueMatcher
{
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
