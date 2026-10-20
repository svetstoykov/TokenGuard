namespace TokenGuard.Core.Summarization;

/// <summary>
///     Defines why a model stopped writing a summary.
/// </summary>
/// <remarks>
///     An <see cref="Abstractions.ILlmSummarizer" /> maps its provider's finish or stop reason to one of these values.
///     TokenGuard reads the value to decide whether to accept the summary, and logs it.
/// </remarks>
public enum SummarizerFinishReason
{
    /// <summary>
    ///     A normal end: the model finished its answer.
    /// </summary>
    Completed,

    /// <summary>
    ///     A cut at <see cref="SummarizerRequest.MaxOutputTokens" />. The summary is accepted, and a warning reports that its
    ///     end can be missing.
    /// </summary>
    OutputLimit,

    /// <summary>
    ///     A refusal to write the summary. The answer is rejected as a failed summary.
    /// </summary>
    Refused,

    /// <summary>
    ///     Any other reason, or no reason reported. A non-blank answer is accepted.
    /// </summary>
    Other,
}
