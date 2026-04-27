namespace Codexplorer.Automation.Protocol;

internal sealed record SubmitResponse(
    string SessionId,
    string Outcome,
    string? AssistantText,
    bool AssistantTextIsPartial,
    int ModelTurnsCompleted,
    int? ReportedTokensConsumed,
    bool SessionOpen,
    bool AsksRunner,
    string? RunnerQuestion,
    string LogFilePath,
    string? DegradationReason,
    SubmitFailure? Failure);
