using Anthropic;
using TokenGuard.Core.Configuration;
using TokenGuard.Core.Options;

namespace TokenGuard.Extensions.Anthropic;

/// <summary>
/// Registers Anthropic-backed LLM summarization on a <see cref="ConversationConfigBuilder"/>.
/// </summary>
/// <remarks>
/// <para>
/// TokenGuard always runs sliding-window masking first. Call <see cref="UseLlmSummarization"/> to add Anthropic as the
/// optional fallback stage when masking still leaves the prepared context over budget.
/// </para>
/// <para>
/// Anthropic's SDK binds the model at request time rather than on <see cref="AnthropicClient"/>, so this extension
/// requires the target <paramref name="model"/> explicitly.
/// </para>
/// </remarks>
public static class AnthropicBuilderExtensions
{
    /// <summary>
    /// Adds Anthropic-backed LLM summarization as the fallback compaction stage for the builder.
    /// </summary>
    /// <param name="builder">The builder to update.</param>
    /// <param name="client">The Anthropic <see cref="AnthropicClient"/> used to generate summaries.</param>
    /// <param name="model">The Anthropic model identifier used for summarization requests.</param>
    /// <param name="options">
    /// Optional summarization options. When omitted, <see cref="LlmSummarizationOptions.Default"/> is used.
    /// </param>
    /// <returns>The same <see cref="ConversationConfigBuilder"/> instance for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="builder"/> or <paramref name="client"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="model"/> is <see langword="null"/>, empty, or whitespace.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when another LLM summarization provider has already been registered on <paramref name="builder"/>.
    /// </exception>
    public static ConversationConfigBuilder UseLlmSummarization(
        this ConversationConfigBuilder builder,
        AnthropicClient client,
        string model,
        LlmSummarizationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        return builder.SetLlmSummarizer(new AnthropicSummarizer(client, model), "Anthropic", options);
    }
}
