using Microsoft.Extensions.DependencyInjection;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Extensions;
using TokenGuard.ReadmeSnippets;
// <LoopUsings>
using OpenAI.Chat;
using TokenGuard.Core.Enums;
using TokenGuard.Extensions.OpenAI;
// </LoopUsings>

// Each tagged block is the text of a README code fence. verify.sh fails when a README stops carrying a block verbatim.
var services = new ServiceCollection();
var chatClient = new ChatClient("gpt-5.4-nano", "placeholder-api-key");
var chatOptions = new ChatCompletionOptions();
var toolExecutor = new ToolExecutor();
var cancellationToken = CancellationToken.None;

// <Register>
services.AddConversationContext(builder => builder
    .WithMaxTokens(25_000)
    .WithCompactionThreshold(0.80));
// </Register>

using var serviceProvider = services.BuildServiceProvider();

// <CreateContext>
using var conversationContext = serviceProvider
    .GetRequiredService<IConversationContextFactory>()
    .Create();
// </CreateContext>

// <Loop>
conversationContext.SetSystemPrompt("You are a precise coding assistant.");
conversationContext.AddPinnedMessage(MessageRole.User, "Repository root is /workspace/project.");
conversationContext.AddUserMessage("Summarize the failing tests.");

while (true)
{
    var prepared = await conversationContext.PrepareAsync(cancellationToken);

    if (prepared.Outcome == PrepareOutcome.CannotCompact)
        throw new InvalidOperationException(prepared.BudgetFailureReason);

    ChatCompletion response = await chatClient.CompleteChatAsync(
        prepared.Messages.ForOpenAI(),
        chatOptions,
        cancellationToken);

    conversationContext.RecordModelResponse(
        response.ResponseSegments(),
        response.InputTokens());

    if (response.ToolCalls.Count == 0)
        break;

    foreach (var toolCall in response.ToolCalls)
    {
        var result = toolExecutor.Execute(toolCall);
        conversationContext.RecordToolResult(toolCall.Id, toolCall.FunctionName, result);
    }
}
// </Loop>
