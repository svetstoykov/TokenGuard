using TokenGuard.Core.Configuration;
using TokenGuard.Core.Defaults;

namespace TokenGuard.Core.Models;

/// <summary>
/// Defines the token budget that governs when a conversation compacts and how much history can be sent.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ContextBudget"/> separates the model's total context window from the portion that can actually be used for
/// recorded messages. The reserved token allowance is subtracted first so callers can hold space for system
/// overhead, response allowance, or provider-specific framing that is not represented as <see cref="ContextMessage"/> values.
/// </para>
/// <para>
/// The threshold properties express policy as fractions of <see cref="AvailableTokens"/> rather than raw counts. This
/// keeps the same compaction behavior portable across models with different context sizes while still exposing the
/// derived integer trigger values used by the runtime.
/// </para>
/// </remarks>
public readonly record struct ContextBudget
{
    /// <summary>
    /// Initializes a <see cref="ContextBudget"/> with validated token limits and compaction thresholds.
    /// </summary>
    /// <remarks>
    /// The constructor validates the total model window, reserved token space, and threshold ordering before the value
    /// participates in record equality. This keeps the record state aligned with the exposed validated properties rather
    /// than capturing duplicate hidden primary-constructor fields.
    /// </remarks>
    /// <param name="maxTokens">The total token capacity of the target model context window.</param>
    /// <param name="compactionThreshold">The fraction of <see cref="AvailableTokens"/> at which normal compaction begins.</param>
    /// <param name="emergencyThreshold">The fraction of <see cref="AvailableTokens"/> at which emergency compaction begins.</param>
    /// <param name="reservedTokens">The token space held back for non-message content.</param>
    public ContextBudget(
        int maxTokens,
        double compactionThreshold = ConversationDefaults.CompactionThreshold,
        double emergencyThreshold = ConversationDefaults.EmergencyThreshold,
        int reservedTokens = ConversationDefaults.ReservedTokens)
    {
        this.MaxTokens = ValidateMaxTokens(maxTokens, nameof(maxTokens));
        this.CompactionThreshold = ValidateCompactionThreshold(compactionThreshold, emergencyThreshold, nameof(compactionThreshold));
        this.EmergencyThreshold = ValidateEmergencyThreshold(emergencyThreshold, compactionThreshold, nameof(emergencyThreshold));
        this.ReservedTokens = ValidateReservedTokens(reservedTokens, maxTokens, nameof(reservedTokens));
    }

    /// <summary>
    /// Gets the total token capacity of the target context window.
    /// </summary>
    public int MaxTokens { get; }

    /// <summary>
    /// Gets the fraction of <see cref="AvailableTokens"/> at which normal compaction starts.
    /// </summary>
    public double CompactionThreshold { get; }

    /// <summary>
    /// Gets the fraction of <see cref="AvailableTokens"/> at which emergency compaction starts.
    /// </summary>
    public double EmergencyThreshold { get; }

    /// <summary>
    /// Gets the token space reserved for content not represented in message history.
    /// </summary>
    public int ReservedTokens { get; }

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
    /// This helper is used by <see cref="ConversationConfigBuilder"/> and by callers that want a sensible budget for a
    /// known model window without choosing compaction thresholds explicitly. The library default threshold policy is
    /// 0.80 compaction, 0.95 emergency, and 0 reserved tokens.
    /// </remarks>
    /// <param name="maxTokens">The total token capacity of the target model context window.</param>
    /// <returns>A <see cref="ContextBudget"/> configured with 0.80 compaction, 0.95 emergency, and 0 reserved tokens.</returns>
    public static ContextBudget For(int maxTokens)
    {
        return ConversationDefaults.CreateBudget(maxTokens);
    }

    private static int ValidateMaxTokens(int value, string paramName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(paramName, "MaxTokens must be greater than zero.");
        }

        return value;
    }

    private static double ValidateCompactionThreshold(double value, double emergency, string paramName)
    {
        if (value <= 0.0 || value > 1.0)
        {
            throw new ArgumentOutOfRangeException(paramName, "CompactionThreshold must be in the range (0.0, 1.0].");
        }

        if (value >= emergency)
        {
            throw new ArgumentOutOfRangeException(paramName, "CompactionThreshold must be less than EmergencyThreshold.");
        }

        return value;
    }

    private static double ValidateEmergencyThreshold(double value, double compaction, string paramName)
    {
        if (value <= 0.0 || value > 1.0)
        {
            throw new ArgumentOutOfRangeException(paramName, "EmergencyThreshold must be in the range (0.0, 1.0].");
        }

        if (compaction >= value)
        {
            throw new ArgumentOutOfRangeException(paramName, "EmergencyThreshold must be greater than CompactionThreshold.");
        }

        return value;
    }

    private static int ValidateReservedTokens(int value, int max, string paramName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(paramName, "ReservedTokens cannot be negative.");
        }

        if (value >= max)
        {
            throw new ArgumentOutOfRangeException(paramName, "ReservedTokens must be less than MaxTokens.");
        }

        return value;
    }
}
