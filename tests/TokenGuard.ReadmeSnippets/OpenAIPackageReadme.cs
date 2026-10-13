// <OpenAIUsings>
using TokenGuard.Core.Extensions;
using TokenGuard.Core.Options;
using TokenGuard.Extensions.OpenAI;
// </OpenAIUsings>
using Microsoft.Extensions.DependencyInjection;
using OpenAI.Chat;
using TokenGuard.Core.Abstractions;

namespace TokenGuard.ReadmeSnippets;

/// <summary>
///     Holds the fragments of the OpenAI package readme so that the snippet check compiles them against the packed packages.
/// </summary>
/// <remarks>
///     Each tagged block is the text of a code fence in <c>src/TokenGuard.Extensions.OpenAI/PackageReadme.md</c>, indented here
///     by the method body. verify.sh removes that indentation and fails when the readme stops carrying a block verbatim.
///     The parameters stand for the values the fragments continue from. The methods are compiled and never called.
/// </remarks>
internal static class OpenAIPackageReadme
{
    /// <summary>
    ///     Asynchronously runs the fragment that opens the readme.
    /// </summary>
    /// <param name="services">The service collection the fragment registers TokenGuard in.</param>
    /// <param name="chatClient">The OpenAI chat client the fragment sends the request through.</param>
    /// <param name="conversation">The conversation context the fragment prepares and records to.</param>
    /// <param name="cancellationToken">
    ///     A <see cref="CancellationToken" /> to observe while waiting for the task to complete.
    /// </param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public static async Task OverviewAsync(
        IServiceCollection services, ChatClient chatClient, IConversationContext conversation, CancellationToken cancellationToken)
    {
        // <OpenAIOverview>
        services.AddConversationContext(builder => builder
            .WithMaxTokens(40_000)
            .UseLlmSummarization(chatClient));

        var prepared = await conversation.PrepareAsync(cancellationToken);
        var messages = prepared.Messages.ForOpenAI();

        ChatCompletion response = await chatClient.CompleteChatAsync(messages, cancellationToken: cancellationToken);
        var segments = response.ResponseSegments();

        // A reply with no text and no tool call cannot be recorded: RecordModelResponse throws for it.
        if (segments.Count == 0)
            throw new InvalidOperationException("The model returned an empty reply.");

        conversation.RecordModelResponse(segments, response.InputTokens());
        // </OpenAIOverview>
    }

    /// <summary>
    ///     Asynchronously runs the three quick-start fragments of the readme in order.
    /// </summary>
    /// <param name="services">The service collection the fragments register TokenGuard in.</param>
    /// <param name="chatClient">The OpenAI chat client the fragments send the request through.</param>
    /// <param name="chatOptions">The chat completion options the fragments send with the request.</param>
    /// <param name="conversation">The conversation context the fragments prepare and record to.</param>
    /// <param name="cancellationToken">
    ///     A <see cref="CancellationToken" /> to observe while waiting for the task to complete.
    /// </param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public static async Task QuickStartAsync(
        IServiceCollection services, ChatClient chatClient, ChatCompletionOptions chatOptions, IConversationContext conversation,
        CancellationToken cancellationToken)
    {
        // <OpenAIRegistration>
        services.AddConversationContext(builder => builder
            .WithMaxTokens(40_000)
            .WithSlidingWindowOptions(new SlidingWindowOptions(windowSize: 12))
            .UseLlmSummarization(chatClient));
        // </OpenAIRegistration>

        // <OpenAIPrepare>
        var prepared = await conversation.PrepareAsync(cancellationToken);
        var messages = prepared.Messages.ForOpenAI();
        // </OpenAIPrepare>

        // <OpenAISend>
        ChatCompletion response = await chatClient.CompleteChatAsync(
            messages,
            chatOptions,
            cancellationToken);
        var segments = response.ResponseSegments();

        // A reply with no text and no tool call cannot be recorded: RecordModelResponse throws for it.
        if (segments.Count == 0)
            throw new InvalidOperationException("The model returned an empty reply.");

        conversation.RecordModelResponse(segments, response.InputTokens());
        // </OpenAISend>
    }
}
