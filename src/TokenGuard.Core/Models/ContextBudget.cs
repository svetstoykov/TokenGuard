using TokenGuard.Core.Configuration;
using TokenGuard.Core.Defaults;

namespace TokenGuard.Core.Models;

/// <summary>
/// Defines the token budget that governs when a conversation compacts and how much history can be sent.
/// </summary>
/// <remarks>
/// <para>
/// The threshold properties express policy as fractions of <see cref="MaxTokens"/> rather than raw counts. This
/// keeps the same compaction behavior portable across models with different context sizes while still exposing the
/// derived integer trigger values used by the runtime.
/// </para>
/// <para>
/// Emergency truncation is opt-out when using the library's default profile. <see cref="ContextBudget.For(int)"/>
/// and <see cref="ConversationConfigBuilder"/> both apply a default <see cref="EmergencyThreshold"/> of
/// <c>1.0</c>, which fires only when the context reaches the absolute token limit and acts as a last-resort safety
/// net. When <see cref="EmergencyThreshold"/> is explicitly set to <see langword="null"/> the runtime never
/// applies the emergency pass; a conversation that the configured compaction strategy cannot bring within budget
/// will instead surface a degraded or exhausted <see cref="Enums.PrepareOutcome"/>. The raw
/// <see cref="ContextBudget"/> constructor defaults <c>emergencyThreshold</c> to <see langword="null"/> for
/// callers that build budgets directly without the library defaults.
/// </para>
/// </remarks>
public readonly record struct ContextBudget
{
    /// <summary>
    /// Initializes a <see cref="ContextBudget"/> with validated token limits and compaction thresholds.
    /// </summary>
    /// <remarks>
    /// The constructor validates the token limit and threshold ordering before the value
    /// participates in record equality. This keeps the record state aligned with the exposed validated properties rather
    /// than capturing duplicate hidden primary-constructor fields.
    /// </remarks>
    /// <param name="maxTokens">The maximum number of tokens allowed in the conversation.</param>
    /// <param name="compactionThreshold">The fraction of <see cref="MaxTokens"/> at which normal compaction begins.</param>
    /// <param name="emergencyThreshold">
    /// The fraction of <see cref="MaxTokens"/> at which emergency truncation begins, or
    /// <see langword="null"/> to disable emergency truncation entirely. Defaults to <see langword="null"/>
    /// in this constructor. Use <see cref="For(int)"/> or <see cref="ConversationConfigBuilder"/> to obtain
    /// a budget with the library's opinionated default of <c>1.0</c>.
    /// </param>
    /// <param name="overrunTolerance">
    /// The fraction of <paramref name="maxTokens"/> by which a prepared result may exceed the budget and still be
    /// considered acceptable. Must be in the range [0.0, 1.0]. Defaults to
    /// <see cref="ConversationDefaults.OverrunTolerance"/> (0.05, meaning 5% of <paramref name="maxTokens"/>).
    /// Pass <c>0.0</c> to restore strict zero-tolerance behavior.
    /// </param>
    public ContextBudget(
        int maxTokens,
        double compactionThreshold = ConversationDefaults.CompactionThreshold,
        double? emergencyThreshold = null,
        double overrunTolerance = ConversationDefaults.OverrunTolerance)
    {
        this.MaxTokens = ValidateMaxTokens(maxTokens, nameof(maxTokens));
        this.CompactionThreshold = ValidateCompactionThreshold(compactionThreshold, emergencyThreshold, nameof(compactionThreshold));
        this.EmergencyThreshold = ValidateEmergencyThreshold(emergencyThreshold, compactionThreshold, nameof(emergencyThreshold));
        this.OverrunTolerance = ValidateOverrunTolerance(overrunTolerance, nameof(overrunTolerance));
    }

    /// <summary>
    /// Gets the maximum number of tokens allowed in the conversation.
    /// </summary>
    public int MaxTokens { get; }

    /// <summary>
    /// Gets the fraction of <see cref="MaxTokens"/> at which normal compaction starts.
    /// </summary>
    public double CompactionThreshold { get; }

    /// <summary>
    /// Gets the fraction of <see cref="MaxTokens"/> at which emergency truncation starts, or
    /// <see langword="null"/> when emergency truncation is disabled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Emergency truncation is destructive: it drops whole turn groups oldest-first until the context fits within the
    /// threshold or nothing further can be removed. The library default profile sets this to <c>1.0</c>, so the
    /// emergency pass only fires when the context reaches the absolute token limit — acting as a last-resort safety net
    /// after the primary compaction strategy has already run.
    /// </para>
    /// <para>
    /// When <see langword="null"/>, the runtime skips the emergency pass entirely after the primary compaction strategy
    /// runs. A conversation that still exceeds the budget at that point surfaces as
    /// <see cref="Enums.PrepareOutcome.Degraded"/> or <see cref="Enums.PrepareOutcome.ContextExhausted"/> instead of
    /// having messages silently dropped. Disable it by calling
    /// <see cref="Configuration.ConversationConfigBuilder.WithoutEmergencyThreshold"/> on the builder.
    /// </para>
    /// </remarks>
    public double? EmergencyThreshold { get; }

    /// <summary>
    /// Gets the overrun tolerance as a fraction of <see cref="MaxTokens"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When non-zero, <see cref="ConversationContext.PrepareAsync"/> accepts a result whose estimated token total
    /// falls between <see cref="MaxTokens"/> and <c>MaxTokens + <see cref="OverrunToleranceTokens"/></c> inclusive,
    /// returning <see cref="Enums.PrepareOutcome.Compacted"/> rather than <see cref="Enums.PrepareOutcome.Degraded"/>.
    /// This is useful when the token estimator is known to have small systematic overestimates for a given provider.
    /// </para>
    /// <para>
    /// Defaults to <see cref="ConversationDefaults.OverrunTolerance"/> (0.05 — 5% of <see cref="MaxTokens"/>).
    /// Pass <c>0.0</c> to disable tolerance and restore strict-budget behavior.
    /// Compaction strategies always target <see cref="MaxTokens"/>; the tolerance only affects the final outcome
    /// classification after all compaction techniques have run.
    /// </para>
    /// </remarks>
    public double OverrunTolerance { get; }

    /// <summary>
    /// Gets the absolute token count corresponding to <see cref="OverrunTolerance"/> applied to <see cref="MaxTokens"/>.
    /// </summary>
    /// <remarks>
    /// Computed as <c>(int)Math.Floor(MaxTokens * OverrunTolerance)</c>. This is the value compared against the
    /// final token total in <see cref="ConversationContext.PrepareAsync"/> after all compaction has run.
    /// </remarks>
    public int OverrunToleranceTokens => (int)Math.Floor(this.MaxTokens * this.OverrunTolerance);

    /// <summary>
    /// Gets the integer token count at which normal compaction should trigger.
    /// </summary>
    public int CompactionTriggerTokens => (int)Math.Floor(this.MaxTokens * this.CompactionThreshold);

    /// <summary>
    /// Gets the integer token count at which emergency truncation should trigger, or <see langword="null"/> when
    /// <see cref="EmergencyThreshold"/> is not configured.
    /// </summary>
    public int? EmergencyTriggerTokens => this.EmergencyThreshold.HasValue
        ? (int)Math.Floor(this.MaxTokens * this.EmergencyThreshold.Value)
        : null; 

    /// <summary>
    /// Creates a <see cref="ContextBudget"/> that uses the library's default threshold policy.
    /// </summary>
    /// <remarks>
    /// This helper is used by <see cref="ConversationConfigBuilder"/> and by callers that want a sensible
    /// budget for a known model window without choosing compaction thresholds explicitly. The library
    /// default threshold policy is 0.80 compaction and a 1.0 emergency threshold — emergency truncation
    /// fires only when the context reaches the absolute token limit and acts as a last-resort safety net.
    /// To construct a budget without emergency truncation, use the constructor directly and pass
    /// <see langword="null"/> for <c>emergencyThreshold</c>.
    /// </remarks>
    /// <param name="maxTokens">The maximum number of tokens allowed in the conversation.</param>
    /// <returns>A <see cref="ContextBudget"/> configured with 0.80 compaction and 1.0 emergency truncation.</returns>
    public static ContextBudget For(int maxTokens) => new(maxTokens, emergencyThreshold: ConversationDefaults.EmergencyThreshold);

    private static int ValidateMaxTokens(int value, string paramName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(paramName, "MaxTokens must be greater than zero.");
        }

        return value;
    }

    private static double ValidateCompactionThreshold(double value, double? emergency, string paramName)
    {
        if (value is <= 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(paramName, "CompactionThreshold must be in the range (0.0, 1.0].");
        }

        if (emergency.HasValue && value >= emergency.Value)
        {
            throw new ArgumentOutOfRangeException(paramName, "CompactionThreshold must be less than EmergencyThreshold.");
        }

        return value;
    }

    private static double? ValidateEmergencyThreshold(double? value, double compaction, string paramName)
    {
        if (!value.HasValue)
            return null;

        if (value.Value is <= 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(paramName, "EmergencyThreshold must be in the range (0.0, 1.0].");
        }

        if (compaction >= value.Value)
        {
            throw new ArgumentOutOfRangeException(paramName, "EmergencyThreshold must be greater than CompactionThreshold.");
        }

        return value;
    }

    private static double ValidateOverrunTolerance(double value, string paramName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(paramName, "OverrunTolerance must be a finite number.");
        }

        if (value is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(paramName, "OverrunTolerance must be in the range [0.0, 1.0].");
        }

        return value;
    }
}
