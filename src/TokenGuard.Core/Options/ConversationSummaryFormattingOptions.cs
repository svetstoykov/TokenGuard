using TokenGuard.Core.Defaults;

namespace TokenGuard.Core.Options;

/// <summary>
/// Configures how LLM summarization transcripts render tool result payloads.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ConversationSummaryFormattingOptions"/> controls the deterministic prompt-shaping layer used before
/// TokenGuard calls an external summarization model. Small tool results stay verbatim, medium results become
/// head/salient/tail excerpts, and very large results collapse to metadata.
/// </para>
/// <para>
/// These thresholds are intentionally independent from <see cref="LlmSummarizationOptions"/>. The latter governs when
/// summarization happens and how much output budget is available, while these values govern how raw history is encoded
/// before the summarizer ever sees it.
/// </para>
/// </remarks>
internal readonly record struct ConversationSummaryFormattingOptions
{
    /// <summary>
    /// Initializes a default <see cref="ConversationSummaryFormattingOptions"/> value using library-defined defaults.
    /// </summary>
    public ConversationSummaryFormattingOptions()
        : this(
            fullToolResultMaxTokens: ConversationSummaryFormattingDefaults.FullToolResultMaxTokens,
            excerptToolResultMaxTokens: ConversationSummaryFormattingDefaults.ExcerptToolResultMaxTokens,
            excerptHeadLineCount: ConversationSummaryFormattingDefaults.ExcerptHeadLineCount,
            excerptTailLineCount: ConversationSummaryFormattingDefaults.ExcerptTailLineCount,
            excerptSalientLineCount: ConversationSummaryFormattingDefaults.ExcerptSalientLineCount)
    {
    }

    /// <summary>
    /// Initializes a <see cref="ConversationSummaryFormattingOptions"/> value with validated bounds.
    /// </summary>
    /// <param name="fullToolResultMaxTokens">The largest estimated tool result size that stays verbatim in the transcript.</param>
    /// <param name="excerptToolResultMaxTokens">
    /// The largest estimated tool result size that uses excerpt formatting. Larger results fall back to metadata only.
    /// </param>
    /// <param name="excerptHeadLineCount">The number of leading lines included in excerpted tool results.</param>
    /// <param name="excerptTailLineCount">The number of trailing lines included in excerpted tool results.</param>
    /// <param name="excerptSalientLineCount">The maximum number of salient middle lines included in excerpted tool results.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when any numeric argument is less than or equal to zero, or when
    /// <paramref name="excerptToolResultMaxTokens"/> is less than <paramref name="fullToolResultMaxTokens"/>.
    /// </exception>
    public ConversationSummaryFormattingOptions(
        int fullToolResultMaxTokens = ConversationSummaryFormattingDefaults.FullToolResultMaxTokens,
        int excerptToolResultMaxTokens = ConversationSummaryFormattingDefaults.ExcerptToolResultMaxTokens,
        int excerptHeadLineCount = ConversationSummaryFormattingDefaults.ExcerptHeadLineCount,
        int excerptTailLineCount = ConversationSummaryFormattingDefaults.ExcerptTailLineCount,
        int excerptSalientLineCount = ConversationSummaryFormattingDefaults.ExcerptSalientLineCount)
    {
        this.FullToolResultMaxTokens = ValidatePositive(fullToolResultMaxTokens, nameof(fullToolResultMaxTokens));
        this.ExcerptToolResultMaxTokens = ValidateExcerptMaxTokens(
            excerptToolResultMaxTokens,
            fullToolResultMaxTokens,
            nameof(excerptToolResultMaxTokens));
        this.ExcerptHeadLineCount = ValidatePositive(excerptHeadLineCount, nameof(excerptHeadLineCount));
        this.ExcerptTailLineCount = ValidatePositive(excerptTailLineCount, nameof(excerptTailLineCount));
        this.ExcerptSalientLineCount = ValidatePositive(excerptSalientLineCount, nameof(excerptSalientLineCount));
    }

    /// <summary>
    /// Gets the default transcript-formatting configuration.
    /// </summary>
    public static ConversationSummaryFormattingOptions Default => new();

    /// <summary>
    /// Gets the largest estimated tool result size that stays verbatim in the transcript.
    /// </summary>
    public int FullToolResultMaxTokens { get; }

    /// <summary>
    /// Gets the largest estimated tool result size that uses excerpt formatting.
    /// </summary>
    public int ExcerptToolResultMaxTokens { get; }

    /// <summary>
    /// Gets the number of leading lines included in excerpted tool results.
    /// </summary>
    public int ExcerptHeadLineCount { get; }

    /// <summary>
    /// Gets the number of trailing lines included in excerpted tool results.
    /// </summary>
    public int ExcerptTailLineCount { get; }

    /// <summary>
    /// Gets the maximum number of salient middle lines included in excerpted tool results.
    /// </summary>
    public int ExcerptSalientLineCount { get; }

    private static int ValidatePositive(int value, string paramName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(paramName, $"{paramName} must be greater than zero.");
        }

        return value;
    }

    private static int ValidateExcerptMaxTokens(int excerptValue, int fullValue, string paramName)
    {
        if (excerptValue <= 0)
        {
            throw new ArgumentOutOfRangeException(paramName, $"{paramName} must be greater than zero.");
        }

        if (excerptValue < fullValue)
        {
            throw new ArgumentOutOfRangeException(paramName, $"{paramName} must be greater than or equal to fullToolResultMaxTokens.");
        }

        return excerptValue;
    }
}
