using System.Text.Json;
using Codexplorer.Agent;
using Codexplorer.Workspace;

namespace Codexplorer.Automation;

internal sealed class AutomationCommandDispatcher : IAutomationCommandDispatcher
{
    private readonly IExplorerAgent _explorerAgent;
    private readonly IWorkspaceManager _workspaceManager;
    private readonly IAutomationSessionRegistry _sessionRegistry;

    public AutomationCommandDispatcher(
        IExplorerAgent explorerAgent,
        IWorkspaceManager workspaceManager,
        IAutomationSessionRegistry sessionRegistry)
    {
        ArgumentNullException.ThrowIfNull(explorerAgent);
        ArgumentNullException.ThrowIfNull(workspaceManager);
        ArgumentNullException.ThrowIfNull(sessionRegistry);

        this._explorerAgent = explorerAgent;
        this._workspaceManager = workspaceManager;
        this._sessionRegistry = sessionRegistry;
    }

    public Task<AutomationResponseEnvelope> DispatchAsync(AutomationRequestEnvelope request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        if (string.Equals(request.Command, "ping", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(
                AutomationResponseEnvelope.SuccessResponse(
                    request.RequestId!,
                    new AutomationPingResult(Status: "ok", ProtocolVersion: 1)));
        }

        if (string.Equals(request.Command, "open_session", StringComparison.OrdinalIgnoreCase))
        {
            return this.OpenSessionAsync(request, ct);
        }

        if (string.Equals(request.Command, "close_session", StringComparison.OrdinalIgnoreCase))
        {
            return this.CloseSessionAsync(request);
        }

        if (string.Equals(request.Command, "submit", StringComparison.OrdinalIgnoreCase))
        {
            return this.SubmitAsync(request, ct);
        }

        return Task.FromResult(
            AutomationResponseEnvelope.ErrorResponse(
                request.RequestId,
                code: "unknown_command",
                message: $"Command '{request.Command}' is not supported."));
    }

    private async Task<AutomationResponseEnvelope> OpenSessionAsync(AutomationRequestEnvelope request, CancellationToken ct)
    {
        if (!TryGetPayloadObject(request, out var payload, out var errorResponse))
        {
            return errorResponse!;
        }

        OpenSessionPayload? openSessionPayload;

        try
        {
            openSessionPayload = payload.Deserialize<OpenSessionPayload>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException ex)
        {
            return AutomationResponseEnvelope.ErrorResponse(
                request.RequestId,
                code: "invalid_request",
                message: $"Command '{request.Command}' payload could not be parsed. {ex.Message}");
        }

        if (openSessionPayload is null)
        {
            return AutomationResponseEnvelope.ErrorResponse(
                request.RequestId,
                code: "invalid_request",
                message: $"Command '{request.Command}' requires a JSON object payload.");
        }

        var hasWorkspacePath = !string.IsNullOrWhiteSpace(openSessionPayload.WorkspacePath);
        var hasRepositoryUrl = !string.IsNullOrWhiteSpace(openSessionPayload.RepositoryUrl);

        if (hasWorkspacePath == hasRepositoryUrl)
        {
            return AutomationResponseEnvelope.ErrorResponse(
                request.RequestId,
                code: "invalid_request",
                message: "Payload must provide exactly one of 'workspacePath' or 'repositoryUrl'.");
        }

        Codexplorer.Workspace.Workspace? workspace;

        if (hasWorkspacePath)
        {
            try
            {
                workspace = this._workspaceManager.FindByLocalPath(openSessionPayload.WorkspacePath!);
            }
            catch (ArgumentException ex)
            {
                return AutomationResponseEnvelope.ErrorResponse(
                    request.RequestId,
                    code: "invalid_request",
                    message: ex.Message);
            }

            if (workspace is null)
            {
                return AutomationResponseEnvelope.ErrorResponse(
                    request.RequestId,
                    code: "workspace_not_found",
                    message: $"No tracked workspace exists at '{Path.GetFullPath(openSessionPayload.WorkspacePath!)}'.");
            }
        }
        else
        {
            try
            {
                workspace = await this._workspaceManager.CloneAsync(openSessionPayload.RepositoryUrl!, ct: ct).ConfigureAwait(false);
            }
            catch (ArgumentException ex)
            {
                return AutomationResponseEnvelope.ErrorResponse(
                    request.RequestId,
                    code: "invalid_request",
                    message: ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return AutomationResponseEnvelope.ErrorResponse(
                    request.RequestId,
                    code: "clone_failed",
                    message: ex.Message);
            }
            catch (RepositoryTooLargeException ex)
            {
                return AutomationResponseEnvelope.ErrorResponse(
                    request.RequestId,
                    code: "clone_failed",
                    message: ex.Message);
            }
        }

        var explorerSession = this._explorerAgent.StartSession(workspace!);

        try
        {
            var registration = this._sessionRegistry.Add(workspace, explorerSession);
            return AutomationResponseEnvelope.SuccessResponse(
                request.RequestId!,
                new OpenSessionResult(
                    registration.SessionId,
                    new AutomationWorkspaceResult(
                        workspace.Name,
                        workspace.OwnerRepo,
                        workspace.LocalPath,
                        workspace.ClonedAt,
                        workspace.SizeBytes),
                    registration.LogFilePath));
        }
        catch
        {
            return await DisposeFailedOpenSessionAsync(explorerSession).ConfigureAwait(false);
        }
    }

    private async Task<AutomationResponseEnvelope> CloseSessionAsync(AutomationRequestEnvelope request)
    {
        if (!TryGetRequiredStringPayloadProperty(request, "sessionId", out var sessionId, out var errorResponse))
        {
            return errorResponse!;
        }

        if (!this._sessionRegistry.TryRemove(sessionId!, out var registration))
        {
            return AutomationResponseEnvelope.ErrorResponse(
                request.RequestId,
                code: "session_not_found",
                message: $"Session '{sessionId}' is not open.");
        }

        await registration!.Session.DisposeAsync().ConfigureAwait(false);

        return AutomationResponseEnvelope.SuccessResponse(
            request.RequestId!,
            new CloseSessionResult(registration.SessionId, Status: "closed"));
    }

    private async Task<AutomationResponseEnvelope> SubmitAsync(AutomationRequestEnvelope request, CancellationToken ct)
    {
        if (!TryGetRequiredStringPayloadProperty(request, "sessionId", out var sessionId, out var sessionErrorResponse))
        {
            return sessionErrorResponse!;
        }

        if (!TryGetRequiredStringPayloadProperty(request, "message", out var message, out var messageErrorResponse))
        {
            return messageErrorResponse!;
        }

        if (!this._sessionRegistry.TryGet(sessionId!, out var registration))
        {
            return AutomationResponseEnvelope.ErrorResponse(
                request.RequestId,
                code: "session_not_found",
                message: $"Session '{sessionId}' is not open.");
        }

        var exchangeResult = await registration!.Session.SubmitAsync(message!, ct).ConfigureAwait(false);
        var submitResponse = CreateSubmitResult(registration, exchangeResult);

        if (IsTerminalOutcome(exchangeResult))
        {
            await this.RemoveAndDisposeSessionAsync(registration.SessionId).ConfigureAwait(false);
        }

        return AutomationResponseEnvelope.SuccessResponse(request.RequestId!, submitResponse);
    }

    private static bool TryGetRequiredStringPayloadProperty(
        AutomationRequestEnvelope request,
        string propertyName,
        out string? value,
        out AutomationResponseEnvelope? errorResponse)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);

        if (request.Payload is null)
        {
            value = null;
            errorResponse = AutomationResponseEnvelope.ErrorResponse(
                request.RequestId,
                code: "invalid_request",
                message: $"Command '{request.Command}' requires a JSON object payload.");
            return false;
        }

        var payload = request.Payload.Value;

        if (payload.ValueKind != JsonValueKind.Object)
        {
            value = null;
            errorResponse = AutomationResponseEnvelope.ErrorResponse(
                request.RequestId,
                code: "invalid_request",
                message: $"Command '{request.Command}' requires a JSON object payload.");
            return false;
        }

        if (!payload.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            value = null;
            errorResponse = AutomationResponseEnvelope.ErrorResponse(
                request.RequestId,
                code: "invalid_request",
                message: $"Payload field '{propertyName}' must be a non-empty string.");
            return false;
        }

        value = property.GetString();

        if (string.IsNullOrWhiteSpace(value))
        {
            errorResponse = AutomationResponseEnvelope.ErrorResponse(
                request.RequestId,
                code: "invalid_request",
                message: $"Payload field '{propertyName}' must be a non-empty string.");
            return false;
        }

        errorResponse = null;
        return true;
    }

    private static bool TryGetPayloadObject(
        AutomationRequestEnvelope request,
        out JsonElement payload,
        out AutomationResponseEnvelope? errorResponse)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Payload is null || request.Payload.Value.ValueKind != JsonValueKind.Object)
        {
            payload = default;
            errorResponse = AutomationResponseEnvelope.ErrorResponse(
                request.RequestId,
                code: "invalid_request",
                message: $"Command '{request.Command}' requires a JSON object payload.");
            return false;
        }

        payload = request.Payload.Value;
        errorResponse = null;
        return true;
    }

    private static async Task<AutomationResponseEnvelope> DisposeFailedOpenSessionAsync(IExplorerSession explorerSession)
    {
        await explorerSession.DisposeAsync().ConfigureAwait(false);
        throw new InvalidOperationException("Failed to register opened automation session.");
    }

    private async Task RemoveAndDisposeSessionAsync(string sessionId)
    {
        if (!this._sessionRegistry.TryRemove(sessionId, out var registration))
        {
            return;
        }

        await registration!.Session.DisposeAsync().ConfigureAwait(false);
    }

    private static SubmitResult CreateSubmitResult(AutomationSessionRegistration registration, AgentExchangeResult exchangeResult)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(exchangeResult);

        return exchangeResult switch
        {
            AgentReplyReceived replyReceived => CreateSubmitResult(
                registration,
                outcome: "reply_received",
                assistantText: replyReceived.ReplyText,
                assistantTextIsPartial: false,
                modelTurnsCompleted: replyReceived.ModelTurnsCompleted,
                reportedTokensConsumed: replyReceived.ReportedTokensConsumed,
                sessionOpen: true,
                degradationReason: null,
                failure: null),

            AgentExchangeDegraded degraded => CreateSubmitResult(
                registration,
                outcome: "degraded",
                assistantText: degraded.PartialText,
                assistantTextIsPartial: !string.IsNullOrWhiteSpace(degraded.PartialText),
                modelTurnsCompleted: degraded.ModelTurnsCompleted,
                reportedTokensConsumed: null,
                sessionOpen: false,
                degradationReason: degraded.Reason,
                failure: null),

            AgentExchangeMaxTurnsReached maxTurnsReached => CreateSubmitResult(
                registration,
                outcome: "max_turns_reached",
                assistantText: maxTurnsReached.PartialText,
                assistantTextIsPartial: !string.IsNullOrWhiteSpace(maxTurnsReached.PartialText),
                modelTurnsCompleted: maxTurnsReached.ModelTurnsCompleted,
                reportedTokensConsumed: null,
                sessionOpen: true,
                degradationReason: null,
                failure: null),

            AgentExchangeCancelled cancelled => CreateSubmitResult(
                registration,
                outcome: "cancelled",
                assistantText: cancelled.PartialText,
                assistantTextIsPartial: !string.IsNullOrWhiteSpace(cancelled.PartialText),
                modelTurnsCompleted: cancelled.ModelTurnsCompleted,
                reportedTokensConsumed: null,
                sessionOpen: false,
                degradationReason: null,
                failure: null),

            AgentExchangeFailed failed => CreateSubmitResult(
                registration,
                outcome: "failed",
                assistantText: null,
                assistantTextIsPartial: false,
                modelTurnsCompleted: failed.ModelTurnsCompleted,
                reportedTokensConsumed: null,
                sessionOpen: false,
                degradationReason: null,
                failure: new SubmitFailure(
                    failed.Exception.GetType().FullName ?? failed.Exception.GetType().Name,
                    failed.Exception.Message)),

            _ => throw new InvalidOperationException($"Unsupported exchange result '{exchangeResult.GetType().Name}'.")
        };
    }

    private static SubmitResult CreateSubmitResult(
        AutomationSessionRegistration registration,
        string outcome,
        string? assistantText,
        bool assistantTextIsPartial,
        int modelTurnsCompleted,
        int? reportedTokensConsumed,
        bool sessionOpen,
        string? degradationReason,
        SubmitFailure? failure)
    {
        var asksRunner = AutomationRunnerQuestion.TryExtract(assistantText, out var runnerQuestion);

        return new SubmitResult(
            registration.SessionId,
            outcome,
            assistantText,
            assistantTextIsPartial,
            modelTurnsCompleted,
            reportedTokensConsumed,
            sessionOpen,
            asksRunner,
            runnerQuestion,
            registration.LogFilePath,
            degradationReason,
            failure);
    }

    private static bool IsTerminalOutcome(AgentExchangeResult exchangeResult)
    {
        return exchangeResult is AgentExchangeDegraded or AgentExchangeCancelled or AgentExchangeFailed;
    }

    private sealed record AutomationPingResult(string Status, int ProtocolVersion);

    private sealed record OpenSessionResult(
        string SessionId,
        AutomationWorkspaceResult Workspace,
        string LogFilePath);

    private sealed record OpenSessionPayload(string? WorkspacePath, string? RepositoryUrl);

    private sealed record AutomationWorkspaceResult(
        string Name,
        string OwnerRepo,
        string LocalPath,
        DateTime ClonedAt,
        long SizeBytes);

    private sealed record CloseSessionResult(string SessionId, string Status);

    private sealed record SubmitResult(
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

    private sealed record SubmitFailure(string ExceptionType, string Message);
}
