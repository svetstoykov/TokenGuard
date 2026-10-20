namespace Codexplorer.Automation.Scoring;

/// <summary>Provides ordinal literal matching with letter and digit boundaries.</summary>
internal static class ValueMatcher
{
    /// <summary>Tries all occurrences, including overlaps, against endpoint boundaries.</summary>
    /// <param name="normalizedText">The cleaned source text.</param>
    /// <param name="normalizedValue">The nonempty cleaned literal.</param>
    /// <returns>Whether an occurrence satisfies both boundaries.</returns>
    public static bool IsMatch(string normalizedText, string normalizedValue) =>
        Codexplorer.Measurements.ProbeText.IsMatch(normalizedText, normalizedValue);
}
