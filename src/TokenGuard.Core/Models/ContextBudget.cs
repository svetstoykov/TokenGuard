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
/// <para>
/// Emergency truncation is opt-in. When <see cref="EmergencyThreshold"/> is <see langword="null"/> — the default — the
/// runtime never applies the emergency pass. Configure it only when hard message-dropping under extreme pressure is
/// acceptable, because the pass removes whole turn groups oldest-first and cannot be reversed. Without it, a conversation
/// that the configured compaction strategy cannot bring within budget will surface a degraded or exhausted
/// <see cref="Enums.PrepareOutcome"/> instead.
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
    /// <param name="emergencyThreshold">
    /// The fraction of <see cref="AvailableTokens"/> at which emergency truncation begins, or
    /// <see langword="null"/> to disable emergency truncation entirely. Defaults to <see langword="null"/>.
    /// </param>
    /// <param name="reservedTokens">The token space held back for non-message content.</param>
    public ContextBudget(
        int maxTokens,
        double compactionThreshold = ConversationDefaults.CompactionThreshold,
        double? emergencyThreshold = null,
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
    /// Gets the fraction of <see cref="AvailableTokens"/> at which emergency truncation starts, or
    /// <see langword="null"/> when emergency truncation is disabled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Emergency truncation is destructive: it drops whole turn groups oldest-first until the context fits within the
    /// threshold or nothing further can be removed. Configure this value only when that behavior is acceptable for the
    /// target use case.
    /// </para>
    /// <para>
    /// When <see langword="null"/>, the runtime skips the emergency pass entirely after the primary compaction strategy
    /// runs. A conversation that still exceeds the budget at that point surfaces as
    /// <see cref="Enums.PrepareOutcome.Degraded"/> or <see cref="Enums.PrepareOutcome.ContextExhausted"/> instead of
    /// having messages silently dropped.
    /// </para>
    /// </remarks>
    public double? EmergencyThreshold { get; }

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
    /// Gets the integer token count at which emergency truncation should trigger, or <see langword="null"/> when
    /// <see cref="EmergencyThreshold"/> is not configured.
    /// </summary>
    public int? EmergencyTriggerTokens => this.EmergencyThreshold.HasValue
        ? (int)Math.Floor(this.AvailableTokens * this.EmergencyThreshold.Value)
        : null;

    /// <summary>
    /// Creates a <see cref="ContextBudget"/> that uses the library's default threshold policy.
    /// </summary>
    /// <remarks>
    /// This helper is used by <see cref="ConversationConfigBuilder"/> and by callers that want a sensible budget for a
    /// known model window without choosing compaction thresholds explicitly. The library default threshold policy is
    /// 0.80 compaction, no emergency truncation, and 0 reserved tokens.
    /// </remarks>
    /// <param name="maxTokens">The total token capacity of the target model context window.</param>
    /// <returns>A <see cref="ContextBudget"/> configured with 0.80 compaction, no emergency truncation, and 0 reserved tokens.</returns>
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
