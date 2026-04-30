using OpenAI.Chat;
using TokenGuard.Core.Configuration;
using TokenGuard.Core.Options;

namespace TokenGuard.Extensions.OpenAI;

/// <summary>
/// Registers OpenAI-backed LLM summarization on a <see cref="ConversationConfigBuilder"/>.
/// </summary>
/// <remarks>
/// <para>
/// TokenGuard always runs sliding-window masking first. Call <see cref="UseLlmSummarization"/> to add OpenAI as the
/// optional fallback stage when masking still leaves the prepared context over budget.
/// </para>
/// <para>
/// Only one LLM summarization provider may be registered per builder instance. Attempting to register both OpenAI and
/// Anthropic on the same builder throws immediately so configuration bugs fail fast at startup.
/// </para>
/// </remarks>
public static class OpenAIBuilderExtensions
{
    /// <summary>
    /// Adds OpenAI-backed LLM summarization as the fallback compaction stage for the builder.
    /// </summary>
    /// <param name="builder">The builder to update.</param>
    /// <param name="client">The OpenAI <see cref="ChatClient"/> used to generate summaries.</param>
    /// <param name="options">
    /// Optional summarization options. When omitted, <see cref="LlmSummarizationOptions.Default"/> is used.
    /// </param>
    /// <returns>The same <see cref="ConversationConfigBuilder"/> instance for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="builder"/> or <paramref name="client"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when another LLM summarization provider has already been registered on <paramref name="builder"/>.
    /// </exception>
    public static ConversationConfigBuilder UseLlmSummarization(
        this ConversationConfigBuilder builder,
        ChatClient client,
        LlmSummarizationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(client);

        return builder.SetLlmSummarizer(() => new OpenAISummarizer(client), "OpenAI", options);
    }
}
