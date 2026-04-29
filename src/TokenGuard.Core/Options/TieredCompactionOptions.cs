using TokenGuard.Core.Defaults;
using TokenGuard.Core.Strategies;

namespace TokenGuard.Core.Options;

/// <summary>
/// Configures the two-stage behavior used by <see cref="TieredCompactionStrategy"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="TieredCompactionOptions"/> composes TokenGuard's masking and summarization strategies instead of
/// introducing a separate set of duplicate tuning knobs. This keeps the public surface small while still allowing
/// callers to forward fully customized <see cref="SlidingWindowOptions"/> and
/// <see cref="LlmSummarizationOptions"/> values.
/// </para>
/// <para>
/// When either embedded options value is omitted, the constructor falls back to that strategy's own
/// <c>Default</c> configuration. This preserves consistency between standalone and tiered usage.
/// </para>
/// </remarks>
public readonly record struct TieredCompactionOptions
{
    /// <summary>
    /// Initializes a <see cref="TieredCompactionOptions"/> value that uses each embedded strategy's default options.
    /// </summary>
    /// <remarks>
    /// This parameterless constructor exists because value types otherwise zero-initialize when created with
    /// <c>new()</c>. Routing through the validating constructor preserves the same default behavior for
    /// explicit construction and <see cref="Default"/>.
    /// </remarks>
    public TieredCompactionOptions()
        : this(slidingWindowOptions: null, llmSummarizationOptions: null)
    {
    }

    /// <summary>
    /// Initializes a <see cref="TieredCompactionOptions"/> value with forwarded inner-strategy configuration.
    /// </summary>
    /// <param name="slidingWindowOptions">
    /// The options forwarded to the inner <see cref="SlidingWindowStrategy"/>, or <see langword="null"/> to use
    /// <see cref="SlidingWindowOptions.Default"/>.
    /// </param>
    /// <param name="llmSummarizationOptions">
    /// The options forwarded to the inner <see cref="LlmSummarizationStrategy"/>, or <see langword="null"/> to use
    /// <see cref="LlmSummarizationOptions.Default"/>.
    /// </param>
    public TieredCompactionOptions(
        SlidingWindowOptions? slidingWindowOptions = null,
        LlmSummarizationOptions? llmSummarizationOptions = null)
    {
        this.SlidingWindowOptions = slidingWindowOptions ?? SlidingWindowOptions.Default;
        this.LlmSummarizationOptions = llmSummarizationOptions ?? LlmSummarizationOptions.Default;
    }

    /// <summary>
    /// Gets the default configuration used by <see cref="TieredCompactionStrategy"/>.
    /// </summary>
    public static TieredCompactionOptions Default => TieredCompactionDefaults.Options;

    /// <summary>
    /// Gets the options forwarded to the inner <see cref="SlidingWindowStrategy"/>.
    /// </summary>
    public SlidingWindowOptions SlidingWindowOptions { get; }

    /// <summary>
    /// Gets the options forwarded to the inner <see cref="LlmSummarizationStrategy"/>.
    /// </summary>
    public LlmSummarizationOptions LlmSummarizationOptions { get; }
}
