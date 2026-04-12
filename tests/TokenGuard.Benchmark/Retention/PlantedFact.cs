namespace TokenGuard.Benchmark.Retention;

/// <summary>
/// Represents one fact intentionally planted into a retention benchmark scenario.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="PlantedFact"/> captures both recall semantics and placement metadata. The synthesizer uses this record to
/// decide where and how the fact appears, while the scorer uses the same definition as ground truth for evaluation.
/// </para>
/// <para>
/// Validation enforces category-specific invariants during construction so malformed benchmark inputs fail early instead
/// of producing misleading retention results later in the pipeline.
/// </para>
/// </remarks>
/// <param name="Id">Stable identifier for the planted fact. Cannot be null or whitespace.</param>
/// <param name="Category">Fact placement pattern used when synthesizing and scoring the scenario.</param>
/// <param name="Question">Recall question that asks for this fact. Cannot be null or whitespace.</param>
/// <param name="GroundTruth">Expected final answer for the fact. Cannot be null or whitespace.</param>
/// <param name="OriginalValue">
/// Original planted answer for <see cref="FactCategory.Superseded"/> facts before replacement. Required for
/// <see cref="FactCategory.Superseded"/> and otherwise must be <see langword="null"/>.
/// </param>
/// <param name="PlantedAtTurn">Zero-based turn index where the fact first appears. Cannot be negative.</param>
/// <param name="SupersededAtTurn">
/// Zero-based turn index where the fact changes to a newer value. Required for <see cref="FactCategory.Superseded"/> and
/// otherwise must be <see langword="null"/>.
/// </param>
/// <param name="DependsOn">
/// Identifier of a prerequisite fact referenced by this fact. Required for <see cref="FactCategory.Relational"/> and
/// otherwise must be <see langword="null"/>.
/// </param>
/// <exception cref="ArgumentException">
/// Thrown when any required string argument is null or whitespace, when category-specific fields are invalid, or when
/// the superseded turn does not occur after the planted turn.
/// </exception>
/// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="PlantedAtTurn"/> is negative.</exception>
public sealed record PlantedFact(
    string Id,
    FactCategory Category,
    string Question,
    string GroundTruth,
    string? OriginalValue,
    int PlantedAtTurn,
    int? SupersededAtTurn,
    string? DependsOn)
{
    /// <summary>
    /// Gets stable identifier for the planted fact.
    /// </summary>
    public string Id { get; } = string.IsNullOrWhiteSpace(Id)
        ? throw new ArgumentException("Fact id cannot be null or whitespace.", nameof(Id))
        : Id;

    /// <summary>
    /// Gets fact placement pattern used by retention benchmark components.
    /// </summary>
    public FactCategory Category { get; } = Category;

    /// <summary>
    /// Gets recall question asked during probe evaluation.
    /// </summary>
    public string Question { get; } = string.IsNullOrWhiteSpace(Question)
        ? throw new ArgumentException("Question cannot be null or whitespace.", nameof(Question))
        : Question;

    /// <summary>
    /// Gets expected final answer used as scoring ground truth.
    /// </summary>
    public string GroundTruth { get; } = string.IsNullOrWhiteSpace(GroundTruth)
        ? throw new ArgumentException("Ground truth cannot be null or whitespace.", nameof(GroundTruth))
        : GroundTruth;

    /// <summary>
    /// Gets original answer planted before superseded fact changes to final value.
    /// </summary>
    /// <remarks>
    /// This property only applies to <see cref="FactCategory.Superseded"/> facts. Other categories keep this value
    /// <see langword="null"/> so <see cref="GroundTruth"/> continues to represent single canonical answer.
    /// </remarks>
    public string? OriginalValue { get; } = ValidateOriginalValue(Category, OriginalValue);

    /// <summary>
    /// Gets zero-based turn index where the fact first appears.
    /// </summary>
    public int PlantedAtTurn { get; } = PlantedAtTurn < 0
        ? throw new ArgumentOutOfRangeException(nameof(PlantedAtTurn), "Planted turn cannot be negative.")
        : PlantedAtTurn;

    /// <summary>
    /// Gets zero-based turn index where the fact changes to a newer value.
    /// </summary>
    /// <remarks>
    /// This property only applies to <see cref="FactCategory.Superseded"/> facts. Other categories keep this value
    /// <see langword="null"/> so the scenario definition remains unambiguous.
    /// </remarks>
    public int? SupersededAtTurn { get; } = ValidateSupersededAtTurn(Category, PlantedAtTurn, SupersededAtTurn);

    /// <summary>
    /// Gets identifier of prerequisite fact referenced by this fact.
    /// </summary>
    /// <remarks>
    /// This property only applies to <see cref="FactCategory.Relational"/> facts. Other categories keep this value
    /// <see langword="null"/> to avoid accidental hidden dependencies.
    /// </remarks>
    public string? DependsOn { get; } = ValidateDependsOn(Category, DependsOn);

    private static string? ValidateOriginalValue(FactCategory category, string? originalValue)
    {
        if (category == FactCategory.Superseded)
        {
            if (string.IsNullOrWhiteSpace(originalValue))
            {
                throw new ArgumentException("Superseded facts must define an original value.", nameof(OriginalValue));
            }

            return originalValue;
        }

        if (originalValue is not null)
        {
            throw new ArgumentException(
                "Only superseded facts can define an original value.",
                nameof(OriginalValue));
        }

        return null;
    }

    private static int? ValidateSupersededAtTurn(FactCategory category, int plantedAtTurn, int? supersededAtTurn)
    {
        if (category == FactCategory.Superseded)
        {
            if (!supersededAtTurn.HasValue)
            {
                throw new ArgumentException("Superseded facts must define a superseded turn.", nameof(SupersededAtTurn));
            }

            if (supersededAtTurn.Value <= plantedAtTurn)
            {
                throw new ArgumentException(
                    "Superseded turn must occur after planted turn.",
                    nameof(SupersededAtTurn));
            }

            return supersededAtTurn.Value;
        }

        if (supersededAtTurn.HasValue)
        {
            throw new ArgumentException(
                "Only superseded facts can define a superseded turn.",
                nameof(SupersededAtTurn));
        }

        return null;
    }

    private static string? ValidateDependsOn(FactCategory category, string? dependsOn)
    {
        if (category == FactCategory.Relational)
        {
            if (string.IsNullOrWhiteSpace(dependsOn))
            {
                throw new ArgumentException("Relational facts must define a dependency id.", nameof(DependsOn));
            }

            return dependsOn;
        }

        if (dependsOn is not null)
        {
            throw new ArgumentException(
                "Only relational facts can define a dependency id.",
                nameof(DependsOn));
        }

        return null;
    }
}
