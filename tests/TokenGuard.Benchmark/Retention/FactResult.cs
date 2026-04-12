namespace TokenGuard.Benchmark.Retention;

/// <summary>
/// Represents per-fact recall scoring detail for one benchmark run.
/// </summary>
/// <remarks>
/// This record captures expected value, observed answer, and pass or fail outcome for a single planted fact. The result
/// is intentionally flat so benchmark reports can aggregate, serialize, and diff individual retention failures without
/// needing access to original scorer internals.
/// </remarks>
/// <param name="FactId">Identifier of planted fact that was scored. Cannot be null or whitespace.</param>
/// <param name="Category">Category of planted fact being evaluated.</param>
/// <param name="Expected">Expected ground-truth answer. Cannot be null or whitespace.</param>
/// <param name="Actual">Actual model response for this fact, or <see langword="null"/> when no answer was extracted.</param>
/// <param name="Passed">Indicates whether the actual answer satisfied the scoring rule.</param>
/// <exception cref="ArgumentException">
/// Thrown when <paramref name="FactId"/> or <paramref name="Expected"/> is null or whitespace.
/// </exception>
public sealed record FactResult(
    string FactId,
    FactCategory Category,
    string Expected,
    string? Actual,
    bool Passed)
{
    /// <summary>
    /// Gets identifier of planted fact that was scored.
    /// </summary>
    public string FactId { get; } = string.IsNullOrWhiteSpace(FactId)
        ? throw new ArgumentException("Fact id cannot be null or whitespace.", nameof(FactId))
        : FactId;

    /// <summary>
    /// Gets category of planted fact being evaluated.
    /// </summary>
    public FactCategory Category { get; } = Category;

    /// <summary>
    /// Gets expected ground-truth answer.
    /// </summary>
    public string Expected { get; } = string.IsNullOrWhiteSpace(Expected)
        ? throw new ArgumentException("Expected answer cannot be null or whitespace.", nameof(Expected))
        : Expected;

    /// <summary>
    /// Gets actual model response for this fact.
    /// </summary>
    public string? Actual { get; } = Actual;

    /// <summary>
    /// Gets value indicating whether actual answer satisfied scoring rule.
    /// </summary>
    public bool Passed { get; } = Passed;
}
