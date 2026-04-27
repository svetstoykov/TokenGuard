using Codexplorer.Automation.Configuration;

namespace Codexplorer.Automation.Runner;

internal sealed record RunnerHelperAiRequest(
    string TaskId,
    RunnerTaskSize TaskSize,
    string WorkspacePath,
    string InitialPrompt,
    string RunnerQuestion,
    string? AssistantText,
    int TurnsConsumed,
    int MaxTurns,
    int WrapUpWindow,
    bool WrapUpSent);
