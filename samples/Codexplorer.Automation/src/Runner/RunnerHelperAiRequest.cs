using Codexplorer.Automation.Configuration;

namespace Codexplorer.Automation.Runner;

/// <summary>Represents original helper context and an optional code to remove at the model boundary.</summary>
/// <param name="TaskId">The task identifier.</param>
/// <param name="TaskSize">The configured task size.</param>
/// <param name="WorkspacePath">The workspace metadata.</param>
/// <param name="InitialPrompt">The original task prompt without an appended probe instruction.</param>
/// <param name="RunnerQuestion">The assistant's original clarification question.</param>
/// <param name="AssistantText">The original reply, or null when unavailable.</param>
/// <param name="TurnsConsumed">The consumed model-call allowance.</param>
/// <param name="MaxTurns">The total allowance.</param>
/// <param name="WrapUpWindow">The reserved finalization window.</param>
/// <param name="WrapUpSent">Whether wrap-up was sent.</param>
/// <param name="ProbeCanary">The code to remove from both outgoing messages, or null without a probe.</param>
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
    bool WrapUpSent,
    string? ProbeCanary = null);
