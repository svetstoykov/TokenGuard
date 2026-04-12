namespace TokenGuard.Benchmark.Retention;

/// <summary>
/// Represents aggregate retention benchmark scoring output for one strategy run.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="RetentionResult"/> exposes both headline metrics and per-fact detail so benchmark consumers can compare
/// retention and token savings without recalculating derived values externally.
/// </para>
/// <para>
/// Validation ensures ratio fields stay consistent with token and fact counts at construction time. This keeps later
/// reporting code simple and prevents impossible benchmark states from leaking into output.
/// </para>
/// </remarks>
/// <param name="ProfileName">Name of profile that was tested. Cannot be null or whitespace.</param>
/// <param name="StrategyName">Name of compaction strategy used for run. Cannot be null or whitespace.</param>
/// <param name="TotalFacts">Number of facts planted in scenario. Cannot be negative.</param>
/// <param name="RecalledFacts">Number of facts scored as recalled. Cannot be negative or greater than total facts.</param>
/// <param name="RetentionScore">Retention ratio computed as recalled facts divided by total facts. Must be between 0.0 and 1.0.</param>
/// <param name="BaselineTokens">Total token count for uncompacted conversation. Must be greater than zero.</param>
/// <param name="ManagedTokens">Total token count for managed conversation. Cannot be negative.</param>
/// <param name="TokenSavingsPercent">
/// Fractional token reduction computed as 1.0 minus managed tokens divided by baseline tokens. Must be less than or equal
/// to 1.0.
/// </param>
/// <param name="FactResults">Per-fact scoring detail. Cannot be null.</param>
/// <exception cref="ArgumentException">
/// Thrown when a required string is null or whitespace, when counts are inconsistent, or when ratio values do not match
/// supplied counts.
/// </exception>
/// <exception cref="ArgumentNullException">Thrown when <paramref name="FactResults"/> is null.</exception>
/// <exception cref="ArgumentOutOfRangeException">Thrown when token or fact counts fall outside allowed bounds.</exception>
public sealed record RetentionResult(
    string ProfileName,
    string StrategyName,
    int TotalFacts,
    int RecalledFacts,
    double RetentionScore,
    int BaselineTokens,
    int ManagedTokens,
    double TokenSavingsPercent,
    IReadOnlyList<FactResult> FactResults)
{
    /// <summary>
    /// Gets name of profile that was tested.
    /// </summary>
    public string ProfileName { get; } = string.IsNullOrWhiteSpace(ProfileName)
        ? throw new ArgumentException("Profile name cannot be null or whitespace.", nameof(ProfileName))
        : ProfileName;

    /// <summary>
    /// Gets name of strategy used for run.
    /// </summary>
    public string StrategyName { get; } = string.IsNullOrWhiteSpace(StrategyName)
        ? throw new ArgumentException("Strategy name cannot be null or whitespace.", nameof(StrategyName))
        : StrategyName;

    /// <summary>
    /// Gets number of facts planted in scenario.
    /// </summary>
    public int TotalFacts { get; } = TotalFacts < 0
        ? throw new ArgumentOutOfRangeException(nameof(TotalFacts), "Total facts cannot be negative.")
        : TotalFacts;

    /// <summary>
    /// Gets number of facts correctly recalled.
    /// </summary>
    public int RecalledFacts { get; } = ValidateRecalledFacts(TotalFacts, RecalledFacts);

    /// <summary>
    /// Gets retention ratio for run.
    /// </summary>
    public double RetentionScore { get; } = ValidateRetentionScore(TotalFacts, RecalledFacts, RetentionScore);

    /// <summary>
    /// Gets uncompacted conversation token count.
    /// </summary>
    public int BaselineTokens { get; } = BaselineTokens <= 0
        ? throw new ArgumentOutOfRangeException(nameof(BaselineTokens), "Baseline tokens must be greater than zero.")
        : BaselineTokens;

    /// <summary>
    /// Gets managed conversation token count after compaction.
    /// </summary>
    public int ManagedTokens { get; } = ManagedTokens < 0
        ? throw new ArgumentOutOfRangeException(nameof(ManagedTokens), "Managed tokens cannot be negative.")
        : ManagedTokens;

    /// <summary>
    /// Gets fractional reduction in tokens relative to uncompacted conversation.
    /// </summary>
    public double TokenSavingsPercent { get; } = ValidateTokenSavingsPercent(BaselineTokens, ManagedTokens, TokenSavingsPercent);

    /// <summary>
    /// Gets per-fact scoring detail for run.
    /// </summary>
    /// <remarks>
    /// The assigned sequence is copied during construction so later external mutations do not change recorded benchmark
    /// results.
    /// </remarks>
    public IReadOnlyList<FactResult> FactResults { get; } = FactResults is null
        ? throw new ArgumentNullException(nameof(FactResults))
        : FactResults.ToArray();

    private static int ValidateRecalledFacts(int totalFacts, int recalledFacts)
    {
        if (recalledFacts < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(RecalledFacts), "Recalled facts cannot be negative.");
        }

        if (recalledFacts > totalFacts)
        {
            throw new ArgumentException("Recalled facts cannot exceed total facts.", nameof(RecalledFacts));
        }

        return recalledFacts;
    }

    private static double ValidateRetentionScore(int totalFacts, int recalledFacts, double retentionScore)
    {
        if (retentionScore is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(RetentionScore), "Retention score must be between 0.0 and 1.0.");
        }

        double expectedScore = totalFacts == 0 ? 0.0 : recalledFacts / (double)totalFacts;

        if (!AreEquivalent(retentionScore, expectedScore))
        {
            throw new ArgumentException("Retention score must equal recalled facts divided by total facts.", nameof(RetentionScore));
        }

        return retentionScore;
    }

    private static double ValidateTokenSavingsPercent(int baselineTokens, int managedTokens, double tokenSavingsPercent)
    {
        if (tokenSavingsPercent > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(TokenSavingsPercent), "Token savings percent cannot exceed 1.0.");
        }

        double expectedSavings = 1.0 - (managedTokens / (double)baselineTokens);

        if (!AreEquivalent(tokenSavingsPercent, expectedSavings))
        {
            throw new ArgumentException(
                "Token savings percent must equal 1.0 minus managed tokens divided by baseline tokens.",
                nameof(TokenSavingsPercent));
        }

        return tokenSavingsPercent;
    }

    private static bool AreEquivalent(double left, double right)
    {
        return Math.Abs(left - right) < 0.0000001;
    }
}
