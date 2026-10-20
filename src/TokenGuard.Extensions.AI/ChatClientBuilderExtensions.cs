using Microsoft.Extensions.AI;
using TokenGuard.Core.Configuration;
using TokenGuard.Core.Options;

namespace TokenGuard.Extensions.AI;

/// <summary>Provides registration of chat-client summarization on a conversation builder.</summary>
/// <remarks>The caller owns the chat client and keeps it alive for every context using the configuration.</remarks>
public static class ChatClientBuilderExtensions
{
    /// <summary>Adds chat-client summarization as the fallback after sliding-window masking.</summary>
    /// <remarks>
    ///     One client serves every context of the configuration and must support concurrent use. The caller controls its model,
    ///     pipeline and network timeout. Cancellation flows from the context's prepare call.
    /// </remarks>
    /// <param name="builder">The builder to configure. Cannot be <see langword="null" />.</param>
    /// <param name="chatClient">The client used for summaries. Cannot be <see langword="null" />.</param>
    /// <param name="options">Summarization options, or <see langword="null" /> for the defaults.</param>
    /// <returns>The same builder instance so that multiple calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder" /> or <paramref name="chatClient" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Another summarizer is already registered.</exception>
    public static ConversationConfigBuilder UseLlmSummarization(
        this ConversationConfigBuilder builder, IChatClient chatClient, LlmSummarizationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(chatClient);
        return builder.UseLlmSummarization(new ChatClientSummarizer(chatClient), options);
    }
}
