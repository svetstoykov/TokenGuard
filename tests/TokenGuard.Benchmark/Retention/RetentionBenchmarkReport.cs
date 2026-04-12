namespace TokenGuard.Benchmark.Retention;

/// <summary>
/// Represents baseline versus managed retention results for one benchmark profile.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RetentionBenchmarkReport"/> keeps both scored runs together so callers can compare recall quality against
/// token reduction without recomputing deltas in reporting code.
/// </para>
/// <para>
/// Validation ensures derived values stay aligned with contained <see cref="RetentionResult"/> instances. This keeps
/// benchmark output internally consistent when reports are serialized or displayed later.
/// </para>
/// </remarks>
/// <param name="ProfileName">Name of profile that produced this report. Cannot be null or whitespace.</param>
/// <param name="Baseline">Baseline retention result for full-history run. Cannot be <see langword="null"/>.</param>
/// <param name="Managed">Managed retention result for compacted run. Cannot be <see langword="null"/>.</param>
/// <param name="RetentionDelta">Managed retention minus baseline retention. Must match contained results.</param>
/// <param name="TokenSavingsPercent">Managed token savings fraction. Must match managed result.</param>
/// <exception cref="ArgumentException">
/// Thrown when <paramref name="ProfileName"/> is null or whitespace or when derived values do not match supplied
/// results.
/// </exception>
/// <exception cref="ArgumentNullException">Thrown when <paramref name="Baseline"/> or <paramref name="Managed"/> is null.</exception>
public sealed record RetentionBenchmarkReport(
    string ProfileName,
    RetentionResult Baseline,
    RetentionResult Managed,
    double RetentionDelta,
    double TokenSavingsPercent)
{
    /// <summary>
    /// Gets name of profile that produced this report.
    /// </summary>
    public string ProfileName { get; } = string.IsNullOrWhiteSpace(ProfileName)
        ? throw new ArgumentException("Profile name cannot be null or whitespace.", nameof(ProfileName))
        : ProfileName;

    /// <summary>
    /// Gets baseline retention result for uncompacted conversation replay.
    /// </summary>
    public RetentionResult Baseline { get; } = Baseline ?? throw new ArgumentNullException(nameof(Baseline));

    /// <summary>
    /// Gets managed retention result for compacted conversation replay.
    /// </summary>
    public RetentionResult Managed { get; } = Managed ?? throw new ArgumentNullException(nameof(Managed));

    /// <summary>
    /// Gets managed retention minus baseline retention.
    /// </summary>
    public double RetentionDelta { get; } = ValidateRetentionDelta(Baseline, Managed, RetentionDelta);

    /// <summary>
    /// Gets managed token savings fraction.
    /// </summary>
    public double TokenSavingsPercent { get; } = ValidateTokenSavingsPercent(Managed, TokenSavingsPercent);

    private static double ValidateRetentionDelta(RetentionResult baseline, RetentionResult managed, double retentionDelta)
    {
        var expectedDelta = managed.RetentionScore - baseline.RetentionScore;

        if (!AreEquivalent(retentionDelta, expectedDelta))
        {
            throw new ArgumentException(
                "Retention delta must equal managed retention score minus baseline retention score.",
                nameof(RetentionDelta));
        }

        return retentionDelta;
    }

    private static double ValidateTokenSavingsPercent(RetentionResult managed, double tokenSavingsPercent)
    {
        if (!AreEquivalent(tokenSavingsPercent, managed.TokenSavingsPercent))
        {
            throw new ArgumentException(
                "Token savings percent must equal managed result token savings percent.",
                nameof(TokenSavingsPercent));
        }

        return tokenSavingsPercent;
    }

    private static bool AreEquivalent(double left, double right)
    {
        return Math.Abs(left - right) < 0.0000001;
    }
}
