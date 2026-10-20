// <CustomSummarizerExample>
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Summarization;

sealed class CustomSummarizer(
    Func<SummarizerRequest, CancellationToken, Task<SummarizerResponse>> callModel) : ILlmSummarizer
{
    public Task<SummarizerResponse> SummarizeAsync(
        SummarizerRequest request, CancellationToken cancellationToken = default)
    {
        return callModel(request, cancellationToken);
    }
}
// </CustomSummarizerExample>

/// <summary>
/// Compiles custom registration against packed packages without access to Core internals.
/// </summary>
internal static class CustomSummarizerRegistration
{
    /// <summary>
    /// Builds a configuration with a consumer-provided model call.
    /// </summary>
    /// <param name="callModel">The model call to invoke for each summary request.</param>
    /// <returns>The configuration using the custom summarizer.</returns>
    public static TokenGuard.Core.Models.ConversationContextConfiguration Build(
        Func<SummarizerRequest, CancellationToken, Task<SummarizerResponse>> callModel) =>
        new TokenGuard.Core.Configuration.ConversationConfigBuilder().WithMaxTokens(40_000)
            .UseLlmSummarization(new CustomSummarizer(callModel)).Build();
}
