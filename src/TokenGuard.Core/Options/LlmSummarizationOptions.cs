using TokenGuard.Core.Defaults;
using TokenGuard.Core.Strategies;

namespace TokenGuard.Core.Options;

/// <summary>
/// Configures how <see cref="LlmSummarizationStrategy"/> protects recent history before summarizing older messages.
/// </summary>
/// <remarks>
/// <see cref="LlmSummarizationOptions"/> intentionally exposes only a hard newest-message floor. This keeps the public
/// contract simple and deterministic: when summarization runs, exactly <see cref="WindowSize"/> newest messages remain
/// verbatim and everything before that boundary is summarized into one synthetic message.
/// </remarks>
public readonly record struct LlmSummarizationOptions
{
    /// <summary>
    /// Initializes a <see cref="LlmSummarizationOptions"/> value with a validated protected tail size.
    /// </summary>
    /// <param name="windowSize">The exact number of newest messages to preserve verbatim when summarization runs.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="windowSize"/> is less than or equal to zero.</exception>
    public LlmSummarizationOptions(int windowSize = LlmSummarizationDefaults.WindowSize)
    {
        this.WindowSize = ValidateWindowSize(windowSize, nameof(windowSize));
    }

    /// <summary>
    /// Gets the default configuration used by <see cref="LlmSummarizationStrategy"/>.
    /// </summary>
    public static LlmSummarizationOptions Default => new(LlmSummarizationDefaults.WindowSize);

    /// <summary>
    /// Gets the exact number of newest messages preserved verbatim at the tail.
    /// </summary>
    public int WindowSize { get; }

    private static int ValidateWindowSize(int value, string paramName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(paramName, "WindowSize must be greater than zero.");
        }

        return value;
    }
}
