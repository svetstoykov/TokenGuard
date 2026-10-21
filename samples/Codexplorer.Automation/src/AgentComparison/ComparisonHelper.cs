using Codexplorer.Automation.AgentComparison.Recording;
using Codexplorer.Automation.Runner;
using OpenAI;
using OpenAI.Chat;

namespace Codexplorer.Automation.AgentComparison;

/// <summary>Answers clarification questions with the original helper prompts through the recorder.</summary>
/// <remarks>This per-session service serializes helper work with the recording scope.</remarks>
internal sealed class ComparisonHelper(IRecordingScope recording) : IComparisonHelper
{
    /// <inheritdoc />
    public async Task<string> AnswerAsync(RuntimeInvocation invocation, string question, string? assistantText, int callsConsumed,
        bool wrapUpSent, CancellationToken cancellationToken)
    {
        if (invocation.RecorderEndpoint.Scheme != "http" || !invocation.RecorderEndpoint.IsLoopback)
            throw new InvalidOperationException("The comparison helper requires a loopback recorder.");
        var task = invocation.Session.Task;
        var request = new RunnerHelperAiRequest(task.TaskId!, task.TaskSize, invocation.Checkout, task.InitialPrompt!, question,
            assistantText, callsConsumed, invocation.Session.MaxAgentCalls, invocation.Session.WrapUpCalls, wrapUpSent);
        var client = new OpenAIClient(new System.ClientModel.ApiKeyCredential(recording.CredentialFor(RecordedRequestKind.Helper)),
            new OpenAIClientOptions { Endpoint = invocation.RecorderEndpoint }).GetChatClient("qwen/qwen3.7-flash");
        var completion = (await client.CompleteChatAsync(OpenRouterRunnerHelperAi.CreateMessages(request),
            new ChatCompletionOptions { MaxOutputTokenCount = 32768 }, cancellationToken).ConfigureAwait(false)).Value;
        var answer = string.Join("\n", completion.Content.Select(part => part.Text).Where(text => !string.IsNullOrWhiteSpace(text)));
        if (string.IsNullOrWhiteSpace(answer))
            throw new InvalidOperationException("The comparison helper returned an empty answer.");
        return answer;
    }
}
