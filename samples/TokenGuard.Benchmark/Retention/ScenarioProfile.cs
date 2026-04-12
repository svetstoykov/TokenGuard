namespace TokenGuard.Samples.Benchmark.Retention;

/// <summary>
/// Represents one retention benchmark scenario definition.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="ScenarioProfile"/> describes conversation size, turn count, fact set, noise theme, and deterministic seed
/// without embedding any synthesis behavior. This keeps benchmark inputs serializable, comparable, and easy to define in
/// built-in batteries or custom tests.
/// </para>
/// <para>
/// The profile defensively copies the fact list during construction so later external collection mutations cannot change
/// benchmark behavior after the scenario has been created.
/// </para>
/// </remarks>
/// <param name="Name">Human-readable scenario label. Cannot be null or whitespace.</param>
/// <param name="TargetTokenCount">Approximate total token volume to synthesize. Must be greater than zero.</param>
/// <param name="TurnCount">Number of user and assistant turn pairs to generate. Must be greater than zero.</param>
/// <param name="Facts">Fact set planted into the synthetic conversation. Cannot be null.</param>
/// <param name="NoiseStyle">Noise template theme used by the synthesizer.</param>
/// <param name="Seed">Deterministic seed used for repeatable synthesis output.</param>
/// <exception cref="ArgumentException">Thrown when <paramref name="Name"/> is null or whitespace.</exception>
/// <exception cref="ArgumentNullException">Thrown when <paramref name="Facts"/> is null.</exception>
/// <exception cref="ArgumentOutOfRangeException">
/// Thrown when <paramref name="TargetTokenCount"/> or <paramref name="TurnCount"/> is less than or equal to zero.
/// </exception>
public sealed record ScenarioProfile(
    string Name,
    int TargetTokenCount,
    int TurnCount,
    IReadOnlyList<PlantedFact> Facts,
    NoiseStyle NoiseStyle,
    int Seed)
{
    /// <summary>
    /// Gets human-readable scenario label.
    /// </summary>
    public string Name { get; } = string.IsNullOrWhiteSpace(Name)
        ? throw new ArgumentException("Profile name cannot be null or whitespace.", nameof(Name))
        : Name;

    /// <summary>
    /// Gets approximate total token volume for synthesized conversation content.
    /// </summary>
    public int TargetTokenCount { get; } = TargetTokenCount <= 0
        ? throw new ArgumentOutOfRangeException(nameof(TargetTokenCount), "Target token count must be greater than zero.")
        : TargetTokenCount;

    /// <summary>
    /// Gets number of user and assistant turn pairs to generate.
    /// </summary>
    public int TurnCount { get; } = TurnCount <= 0
        ? throw new ArgumentOutOfRangeException(nameof(TurnCount), "Turn count must be greater than zero.")
        : TurnCount;

    /// <summary>
    /// Gets all facts that must be planted into the scenario.
    /// </summary>
    /// <remarks>
    /// The assigned sequence is copied during construction so the profile remains immutable even when the caller built it
    /// from a mutable list.
    /// </remarks>
    public IReadOnlyList<PlantedFact> Facts { get; } = Facts is null
        ? throw new ArgumentNullException(nameof(Facts))
        : Facts.ToArray();

    /// <summary>
    /// Gets noise template theme used for non-fact turns.
    /// </summary>
    public NoiseStyle NoiseStyle { get; } = NoiseStyle;

    /// <summary>
    /// Gets deterministic seed used for reproducible synthesis output.
    /// </summary>
    public int Seed { get; } = Seed;
}
