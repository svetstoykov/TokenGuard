namespace TokenGuard.Benchmark.AgentWorkflow.Tasks;

/// <summary>
/// Describes one E2E agent-loop task: how to seed its workspace, what to tell the model,
/// and how to assert the outcome once the loop finishes.
/// </summary>
public sealed class AgentLoopTaskDefinition(
    string name,
    string conversationName,
    string systemPrompt,
    string userMessage,
    string completionMarker,
    Func<string, Task> seedWorkspaceAsync,
    Func<string, string?, Task> assertOutcomeAsync)
{
    public string Name { get; } = name;
    public string ConversationName { get; } = conversationName;
    public string SystemPrompt { get; } = systemPrompt;
    public string UserMessage { get; } = userMessage;
    public string CompletionMarker { get; } = completionMarker;
    public Func<string, Task> SeedWorkspaceAsync { get; } = seedWorkspaceAsync;
    public Func<string, string?, Task> AssertOutcomeAsync { get; } = assertOutcomeAsync;

    public override string ToString() => this.Name;
}
