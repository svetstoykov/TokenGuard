using System.Text.Json;
using Codexplorer.Measurements;

namespace Codexplorer.Automation.Protocol;

/// <summary>Represents the protocol version and effective allowlisted sample settings.</summary>
/// <param name="Status">The ping status.</param>
/// <param name="ProtocolVersion">The protocol version.</param>
/// <param name="Settings">The effective sample settings.</param>
internal sealed record AutomationPingResult(string Status, int ProtocolVersion, EffectiveSettings? Settings = null);

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

    /// <summary>Gets the full commit SHA the workspace is checked out at, or <see langword="null" /> to use the default branch.</summary>
    public string? RepositoryCommit { get; init; }

    /// <summary>Gets the hard allowance for all started model calls in the task.</summary>
    public int? ModelCallBudget { get; init; }

    /// <summary>Gets the optional provider-call window reserved for the wrap-up prompt.</summary>
    public int? WrapUpWindow { get; init; }

    /// <summary>Gets the absolute directory that receives everything the session writes.</summary>
    public string? SessionDirectory { get; init; }

    /// <summary>Gets a value indicating whether the session records every model call in its capture folder.</summary>
    public bool Capture { get; init; }
}

internal sealed record OpenSessionResponse(
    string SessionId,
    AutomationWorkspace Workspace,
    string LogFilePath);

internal sealed record CloseSessionRequest(string SessionId);

/// <summary>Represents the session status and final snapshot after disposal.</summary>
/// <param name="SessionId">The session identifier.</param>
/// <param name="Status">The close status.</param>
/// <param name="Measurements">The final cumulative session measurements.</param>
internal sealed record CloseSessionResponse(string SessionId, string Status, SessionMeasurements? Measurements = null);

internal sealed record SubmitRequest(string SessionId, string Message);

internal sealed record SubmitFailure(string ExceptionType, string Message);

/// <summary>Represents an exchange outcome and cumulative session measurements.</summary>
/// <param name="SessionId">The session identifier.</param>
/// <param name="Outcome">The terminal exchange outcome.</param>
/// <param name="AssistantText">The assistant text returned for the exchange.</param>
/// <param name="AssistantTextIsPartial">Whether the assistant text is partial.</param>
/// <param name="ModelTurnsCompleted">The completed turns in the exchange.</param>
/// <param name="ReportedTokensConsumed">The optional reported token count.</param>
/// <param name="SessionOpen">Whether the session remains open.</param>
/// <param name="AsksRunner">Whether the assistant asks the runner a question.</param>
/// <param name="RunnerQuestion">The optional runner question.</param>
/// <param name="LogFilePath">The session transcript path.</param>
/// <param name="BudgetFailureReason">The optional context-budget failure reason.</param>
/// <param name="Failure">The optional exchange failure.</param>
/// <param name="Measurements">The latest cumulative session measurements.</param>
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
    string? BudgetFailureReason,
    SubmitFailure? Failure,
    SessionMeasurements? Measurements = null);
