using TokenGuard.Core.Abstractions;

namespace TokenGuard.Core.Summarization;

/// <summary>
///     Represents one summarization request that an <see cref="ILlmSummarizer" /> sends to a model.
/// </summary>
/// <remarks>
///     TokenGuard builds the request: the system prompt holds the summary instructions, and the user prompt holds the target
///     size and the transcript of the history to summarize. Send both as given.
/// </remarks>
public sealed class SummarizerRequest
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="SummarizerRequest" /> class.
    /// </summary>
    /// <param name="systemPrompt">The instructions to send as the system message. Cannot be blank.</param>
    /// <param name="userPrompt">The text to send as the one user message. Cannot be blank.</param>
    /// <param name="maxOutputTokens">The largest number of tokens the model may write. Must be greater than zero.</param>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="systemPrompt" /> or <paramref name="userPrompt" /> is <see langword="null" />.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     <paramref name="systemPrompt" /> or <paramref name="userPrompt" /> is empty or whitespace.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxOutputTokens" /> is zero or negative.</exception>
    public SummarizerRequest(string systemPrompt, string userPrompt, int maxOutputTokens)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(userPrompt);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxOutputTokens);

        this.SystemPrompt = systemPrompt;
        this.UserPrompt = userPrompt;
        this.MaxOutputTokens = maxOutputTokens;
    }

    /// <summary>
    ///     Gets the instructions to send as the system or instructions message.
    /// </summary>
    public string SystemPrompt { get; }

    /// <summary>
    ///     Gets the text to send as the one user message: the target size and the transcript to summarize.
    /// </summary>
    public string UserPrompt { get; }

    /// <summary>
    ///     Gets the largest number of tokens the model may write, to send as the output limit of the call.
    /// </summary>
    /// <remarks>
    ///     The cap is twice the target size stated in <see cref="UserPrompt" />, because reasoning tokens count against it.
    /// </remarks>
    public int MaxOutputTokens { get; }
}
