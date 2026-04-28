using System.Text.Json;

namespace Codexplorer.Automation.Protocol;

internal sealed record AutomationPingResult(string Status, int ProtocolVersion);

internal sealed record AutomationProtocolError(string Code, string Message);

internal sealed record AutomationRequestEnvelope(string RequestId, string Command, object? Payload);

internal sealed record AutomationResponseEnvelope(
    string? RequestId,
    bool Success,
    JsonElement? Result,
    AutomationProtocolError? Error);

internal sealed record AutomationWorkspace(
    string Name,
    string OwnerRepo,
    string LocalPath,
    DateTime ClonedAt,
    long SizeBytes);

internal sealed record OpenSessionRequest
{
    public string? WorkspacePath { get; init; }

    public string? RepositoryUrl { get; init; }
}

internal sealed record OpenSessionResponse(
    string SessionId,
    AutomationWorkspace Workspace,
    string LogFilePath);

internal sealed record CloseSessionRequest(string SessionId);

internal sealed record CloseSessionResponse(string SessionId, string Status);

internal sealed record SubmitRequest(string SessionId, string Message);

internal sealed record SubmitFailure(string ExceptionType, string Message);

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
