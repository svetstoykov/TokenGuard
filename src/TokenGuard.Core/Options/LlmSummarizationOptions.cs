using TokenGuard.Core.Defaults;
using TokenGuard.Core.Strategies;

namespace TokenGuard.Core.Options;

/// <summary>
/// Configures how <see cref="LlmSummarizationStrategy"/> protects recent history and bounds the summary token budget.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="LlmSummarizationOptions"/> controls three aspects of summarization policy. <see cref="WindowSize"/>
/// determines how many newest messages remain verbatim. <see cref="MinSummaryTokens"/> sets the floor below which
/// summarization is skipped entirely rather than forwarded with a near-zero budget. <see cref="MaxSummaryTokens"/>
/// caps the target passed to the summarizer so it is never asked to fill an unbounded remainder.
/// </para>
/// <para>
/// The policy boundary belongs here, not inside individual <see cref="TokenGuard.Core.Abstractions.ILlmSummarizer"/>
/// implementations. The strategy enforces system-wide compaction policy; the summarizer produces the best summary
/// for whatever target it receives.
/// </para>
/// </remarks>
public readonly record struct LlmSummarizationOptions
{
    /// <summary>
    /// Initializes a default <see cref="LlmSummarizationOptions"/> value using library-defined defaults.
    /// </summary>
    /// <remarks>
    /// This parameterless constructor exists because value types are always default-initializable. Routing through
    /// the validating constructor preserves consistent default behavior for both explicit and implicit construction.
    /// </remarks>
    public LlmSummarizationOptions()
        : this(
            windowSize: LlmSummarizationDefaults.WindowSize,
            minSummaryTokens: LlmSummarizationDefaults.MinSummaryTokens,
            maxSummaryTokens: LlmSummarizationDefaults.MaxSummaryTokens)
    {
    }

    /// <summary>
    /// Initializes a <see cref="LlmSummarizationOptions"/> value with validated bounds.
    /// </summary>
    /// <param name="windowSize">The exact number of newest messages to preserve verbatim when summarization runs.</param>
    /// <param name="minSummaryTokens">
    /// The minimum remaining token budget required before a summarization request is issued. When the budget falls
    /// below this value the strategy returns only the protected tail without calling the summarizer.
    /// </param>
    /// <param name="maxSummaryTokens">
    /// The maximum token budget forwarded to the summarizer as a target. The actual target is
    /// <c>Math.Min(remainingBudget, maxSummaryTokens)</c>, so summaries are never asked to fill an
    /// unbounded remainder.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="windowSize"/> or <paramref name="minSummaryTokens"/> is less than or equal to
    /// zero, when <paramref name="maxSummaryTokens"/> is less than or equal to zero, or when
    /// <paramref name="maxSummaryTokens"/> is less than <paramref name="minSummaryTokens"/>.
    /// </exception>
    public LlmSummarizationOptions(
        int windowSize = LlmSummarizationDefaults.WindowSize,
        int minSummaryTokens = LlmSummarizationDefaults.MinSummaryTokens,
        int maxSummaryTokens = LlmSummarizationDefaults.MaxSummaryTokens)
    {
        this.WindowSize = ValidateWindowSize(windowSize, nameof(windowSize));
        this.MinSummaryTokens = ValidatePositive(minSummaryTokens, nameof(minSummaryTokens));
        this.MaxSummaryTokens = ValidateMaxSummaryTokens(maxSummaryTokens, minSummaryTokens, nameof(maxSummaryTokens));
    }

    /// <summary>
    /// Gets the default configuration used by <see cref="LlmSummarizationStrategy"/>.
    /// </summary>
    public static LlmSummarizationOptions Default => new();

    /// <summary>
    /// Gets the exact number of newest messages preserved verbatim at the tail.
    /// </summary>
    public int WindowSize { get; }

    /// <summary>
    /// Gets the minimum remaining token budget required before a summarization request is issued.
    /// </summary>
    /// <remarks>
    /// When <c>availableTokens - protectedTailTokens</c> is less than this value, the strategy returns only the
    /// protected tail without invoking the summarizer.
    /// </remarks>
    public int MinSummaryTokens { get; }

    /// <summary>
    /// Gets the maximum token budget forwarded to the summarizer as a target.
    /// </summary>
    /// <remarks>
    /// The actual target passed to <see cref="TokenGuard.Core.Abstractions.ILlmSummarizer"/> is
    /// <c>Math.Min(remainingBudget, MaxSummaryTokens)</c>. This prevents the summarizer from being asked to fill
    /// an arbitrarily large remainder when the available budget vastly exceeds what a useful summary needs.
    /// </remarks>
    public int MaxSummaryTokens { get; }

    private static int ValidateWindowSize(int value, string paramName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(paramName, "WindowSize must be greater than zero.");
        }

        return value;
    }

    private static int ValidatePositive(int value, string paramName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(paramName, $"{paramName} must be greater than zero.");
        }

        return value;
    }

    private static int ValidateMaxSummaryTokens(int maxValue, int minValue, string paramName)
    {
        if (maxValue <= 0)
        {
            throw new ArgumentOutOfRangeException(paramName, $"{paramName} must be greater than zero.");
        }

        if (maxValue < minValue)
        {
            throw new ArgumentOutOfRangeException(paramName, $"{paramName} must be greater than or equal to MinSummaryTokens.");
        }

        return maxValue;
    }
}
