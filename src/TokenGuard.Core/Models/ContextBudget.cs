namespace TokenGuard.Core.Models;

/// <summary>
/// Defines the token budget that governs when a conversation compacts and how much history can be sent.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ContextBudget"/> separates the model's total context window from the portion that can actually be used for
/// recorded messages. <paramref name="reservedTokens"/> is subtracted first so callers can hold space for system
/// overhead, response allowance, or provider-specific framing that is not represented as <see cref="ContextMessage"/> values.
/// </para>
/// <para>
/// The threshold properties express policy as fractions of <see cref="AvailableTokens"/> rather than raw counts. This
/// keeps the same compaction behavior portable across models with different context sizes while still exposing the
/// derived integer trigger values used by the runtime.
/// </para>
/// </remarks>
/// <param name="maxTokens">The total token capacity of the target model context window.</param>
/// <param name="compactionThreshold">The fraction of <see cref="AvailableTokens"/> at which normal compaction begins.</param>
/// <param name="emergencyThreshold">The fraction of <see cref="AvailableTokens"/> at which emergency compaction begins.</param>
/// <param name="reservedTokens">The token space held back for non-message content.</param>
public readonly record struct ContextBudget(
    int maxTokens,
    double compactionThreshold = 0.80,
    double emergencyThreshold = 0.95,
    int reservedTokens = 0)
{
    /// <summary>
    /// Gets the total token capacity of the target context window.
    /// </summary>
    public int MaxTokens { get; } = ValidateMaxTokens(maxTokens);

    /// <summary>
    /// Gets the fraction of <see cref="AvailableTokens"/> at which normal compaction starts.
    /// </summary>
    public double CompactionThreshold { get; } = ValidateCompactionThreshold(compactionThreshold, emergencyThreshold);

    /// <summary>
    /// Gets the fraction of <see cref="AvailableTokens"/> at which emergency compaction starts.
    /// </summary>
    public double EmergencyThreshold { get; } = ValidateEmergencyThreshold(emergencyThreshold, compactionThreshold);

    /// <summary>
    /// Gets the token space reserved for content not represented in message history.
    /// </summary>
    public int ReservedTokens { get; } = ValidateReservedTokens(reservedTokens, maxTokens);

    /// <summary>
    /// Gets the token budget remaining for recorded message history after reservations are applied.
    /// </summary>
    public int AvailableTokens => this.MaxTokens - this.ReservedTokens;

    /// <summary>
    /// Gets the integer token count at which normal compaction should trigger.
    /// </summary>
    public int CompactionTriggerTokens => (int)Math.Floor(this.AvailableTokens * this.CompactionThreshold);

    /// <summary>
    /// Gets the integer token count at which emergency compaction should trigger.
    /// </summary>
    public int EmergencyTriggerTokens => (int)Math.Floor(this.AvailableTokens * this.EmergencyThreshold);

    /// <summary>
    /// Creates a <see cref="ContextBudget"/> that uses the library's default threshold policy.
    /// </summary>
    /// <remarks>
    /// This helper is used by <see cref="ConversationContextConfigurationBuilder"/> and by callers that want a sensible budget for a
    /// known model window without choosing compaction thresholds explicitly.
    /// </remarks>
    /// <param name="maxTokens">The total token capacity of the target model context window.</param>
    /// <returns>A <see cref="ContextBudget"/> configured with default thresholds and no reserved tokens.</returns>
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
