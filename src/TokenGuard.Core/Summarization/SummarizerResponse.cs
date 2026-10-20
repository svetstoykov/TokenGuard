using TokenGuard.Core.Abstractions;

namespace TokenGuard.Core.Summarization;

/// <summary>
///     Represents what a model returned for one <see cref="SummarizerRequest" />.
/// </summary>
/// <remarks>
///     <para>
///         An <see cref="ILlmSummarizer" /> returns the model's text as it arrived; TokenGuard trims it and checks it. For a
///         <see cref="SummarizerFinishReason.Refused" /> answer, <see cref="Text" /> holds the refusal text, which TokenGuard
///         puts in the message of the exception it records.
///     </para>
///     <para>
///         Set a token count only when the provider reported it; leave it <see langword="null" /> otherwise. The counts appear
///         in logs and traces only.
///     </para>
/// </remarks>
/// <param name="Text">The text the model wrote, or the refusal text for a refused request.</param>
/// <param name="FinishReason">The reason the model stopped writing.</param>
public sealed record SummarizerResponse(string Text, SummarizerFinishReason FinishReason)
{
    /// <summary>
    ///     Gets the input tokens the provider reported for the call, or <see langword="null" /> when not reported.
    /// </summary>
    public int? InputTokens { get; init; }

    /// <summary>
    ///     Gets the output tokens the provider reported for the call, or <see langword="null" /> when not reported.
    /// </summary>
    public int? OutputTokens { get; init; }

    /// <summary>
    ///     Gets the reasoning tokens the provider reported within the output tokens, or <see langword="null" /> when not reported.
    /// </summary>
    public int? ReasoningTokens { get; init; }
}
