namespace SemanticFold.Core.Models;

/// <summary>
/// Defines context window limits and compaction trigger thresholds.
/// </summary>
/// <param name="maxTokens">The hard token ceiling for the full context window.</param>
/// <param name="compactionThreshold">The fraction of available tokens at which normal compaction starts.</param>
/// <param name="emergencyThreshold">The fraction of available tokens at which emergency compaction starts.</param>
/// <param name="reservedTokens">Tokens reserved for fixed, non-message content.</param>
public readonly record struct ContextBudget(
    int maxTokens,
    double compactionThreshold = 0.80,
    double emergencyThreshold = 0.95,
    int reservedTokens = 0)
{
    /// <summary>
    /// Gets the hard token ceiling for the full context window.
    /// </summary>
    public int MaxTokens { get; } = ValidateMaxTokens(maxTokens);

    /// <summary>
    /// Gets the fraction of available tokens at which normal compaction starts.
    /// </summary>
    public double CompactionThreshold { get; } = ValidateCompactionThreshold(compactionThreshold, emergencyThreshold);

    /// <summary>
    /// Gets the fraction of available tokens at which emergency compaction starts.
    /// </summary>
    public double EmergencyThreshold { get; } = ValidateEmergencyThreshold(emergencyThreshold, compactionThreshold);

    /// <summary>
    /// Gets the number of tokens reserved for fixed, non-message content.
    /// </summary>
    public int ReservedTokens { get; } = ValidateReservedTokens(reservedTokens, maxTokens);

    /// <summary>
    /// Gets the token budget available for message history.
    /// </summary>
    public int AvailableTokens => this.MaxTokens - this.ReservedTokens;

    /// <summary>
    /// Gets the token count at which normal compaction should trigger.
    /// </summary>
    public int CompactionTriggerTokens => (int)Math.Floor(this.AvailableTokens * this.CompactionThreshold);

    /// <summary>
    /// Gets the token count at which emergency compaction should trigger.
    /// </summary>
    public int EmergencyTriggerTokens => (int)Math.Floor(this.AvailableTokens * this.EmergencyThreshold);

    /// <summary>
    /// Creates a context budget with default thresholds and no reserved tokens.
    /// </summary>
    /// <param name="maxTokens">The hard token ceiling for the full context window.</param>
    /// <returns>A new <see cref="ContextBudget"/> instance using default settings.</returns>
    public static ContextBudget For(int maxTokens)
    {
        return new ContextBudget(maxTokens);
    }

    private static int ValidateMaxTokens(int value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTokens), "MaxTokens must be greater than zero.");
        }

        return value;
    }

    private static double ValidateCompactionThreshold(double value, double emergency)
    {
        if (value <= 0.0 || value > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(compactionThreshold), "CompactionThreshold must be in the range (0.0, 1.0].");
        }

        if (value >= emergency)
        {
            throw new ArgumentOutOfRangeException(nameof(compactionThreshold), "CompactionThreshold must be less than EmergencyThreshold.");
        }

        return value;
    }

    private static double ValidateEmergencyThreshold(double value, double compaction)
    {
        if (value <= 0.0 || value > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(emergencyThreshold), "EmergencyThreshold must be in the range (0.0, 1.0].");
        }

        if (compaction >= value)
        {
            throw new ArgumentOutOfRangeException(nameof(emergencyThreshold), "EmergencyThreshold must be greater than CompactionThreshold.");
        }

        return value;
    }

    private static int ValidateReservedTokens(int value, int max)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(reservedTokens), "ReservedTokens cannot be negative.");
        }

        if (value >= max)
        {
            throw new ArgumentOutOfRangeException(nameof(reservedTokens), "ReservedTokens must be less than MaxTokens.");
        }

        return value;
    }
}
