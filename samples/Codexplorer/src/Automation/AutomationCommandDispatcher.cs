using System.Text.Json;
using Codexplorer.Agent;
using Codexplorer.Diagnostics;
using Codexplorer.Measurements;
using Codexplorer.Workspace;

namespace Codexplorer.Automation;

/// <summary>Dispatches sample commands and finalizes terminal session measurements.</summary>
/// <remarks>The host serializes commands, and the collector rejects overlapping sessions before context creation.</remarks>
internal sealed class AutomationCommandDispatcher : IAutomationCommandDispatcher
{
    private readonly IExplorerAgent _explorerAgent;
    private readonly IWorkspaceManager _workspaceManager;
    private readonly IAutomationSessionRegistry _sessionRegistry;
    private readonly ISessionMeasurementCollector _collector;
    private readonly EffectiveSettings _settings;

    /// <summary>Initializes a new instance of the <see cref="AutomationCommandDispatcher" /> class.</summary>
    /// <param name="explorerAgent">The repository exploration service.</param>
    /// <param name="workspaceManager">The repository workspace service.</param>
    /// <param name="sessionRegistry">The active session registry.</param>
    /// <param name="collector">The singleton session measurement collector.</param>
    /// <param name="settings">The allowlisted effective sample settings.</param>
    public AutomationCommandDispatcher(
        IExplorerAgent explorerAgent,
        IWorkspaceManager workspaceManager,
        IAutomationSessionRegistry sessionRegistry,
        ISessionMeasurementCollector collector,
        EffectiveSettings settings)
    {
        ArgumentNullException.ThrowIfNull(explorerAgent);
        ArgumentNullException.ThrowIfNull(workspaceManager);
        ArgumentNullException.ThrowIfNull(sessionRegistry);
        ArgumentNullException.ThrowIfNull(collector);
        ArgumentNullException.ThrowIfNull(settings);

        this._explorerAgent = explorerAgent;
        this._workspaceManager = workspaceManager;
        this._sessionRegistry = sessionRegistry;
        this._collector = collector;
        this._settings = settings;
    }

    /// <inheritdoc />
    public Task<AutomationResponseEnvelope> DispatchAsync(AutomationRequestEnvelope request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        if (string.Equals(request.Command, "ping", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(
                AutomationResponseEnvelope.SuccessResponse(
                    request.RequestId!,
                    new AutomationPingResult(Status: "ok", ProtocolVersion: 1, this._settings)));
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

        if (hasWorkspacePath)
        {
            return AutomationResponseEnvelope.ErrorResponse(
                request.RequestId,
                code: "invalid_request",
                message: "Payload property 'workspacePath' is not supported in automation mode. Provide 'repositoryUrl' only.");
        }

        if (!hasRepositoryUrl)
        {
            return AutomationResponseEnvelope.ErrorResponse(
                request.RequestId,
                code: "invalid_request",
                message: "Payload must provide 'repositoryUrl'.");
        }

        if (openSessionPayload.ModelCallBudget is < 0)
            return AutomationResponseEnvelope.ErrorResponse(request.RequestId, "invalid_request", "Model-call budget must be nonnegative.");

        if (openSessionPayload.WrapUpWindow is { } window
            && (openSessionPayload.ModelCallBudget is null || window <= 0 || window >= openSessionPayload.ModelCallBudget))
            return AutomationResponseEnvelope.ErrorResponse(
                request.RequestId, "invalid_request", "Wrap-up window must be positive and smaller than the model-call budget.");

        if (!string.IsNullOrWhiteSpace(openSessionPayload.SessionDirectory) && !Path.IsPathRooted(openSessionPayload.SessionDirectory))
            return AutomationResponseEnvelope.ErrorResponse(
                request.RequestId, "invalid_request", "Payload property 'sessionDirectory' must be an absolute path.");

        if (this._collector.IsActive)
            return AutomationResponseEnvelope.ErrorResponse(request.RequestId, "session_already_open", "Only one automation session may be open.");

        try
        {
            this._collector.Begin(openSessionPayload.ModelCallBudget);
        }
        catch (InvalidOperationException)
        {
            return AutomationResponseEnvelope.ErrorResponse(request.RequestId, "session_already_open", "Only one automation session may be open.");
        }

        Codexplorer.Workspace.Workspace? workspace;

        try
        {
            var commitSha = string.IsNullOrWhiteSpace(openSessionPayload.RepositoryCommit) ? null : openSessionPayload.RepositoryCommit;
            workspace = await this._workspaceManager.CloneAsync(openSessionPayload.RepositoryUrl!, commitSha: commitSha, ct: ct)
                .ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            this._collector.End();
            return AutomationResponseEnvelope.ErrorResponse(
                request.RequestId,
                code: "invalid_request",
                message: ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            this._collector.End();
            return AutomationResponseEnvelope.ErrorResponse(
                request.RequestId,
                code: "clone_failed",
                message: ex.Message);
        }
        catch (RepositoryTooLargeException ex)
        {
            this._collector.End();
            return AutomationResponseEnvelope.ErrorResponse(
                request.RequestId,
                code: "clone_failed",
                message: ex.Message);
        }
        catch
        {
            this._collector.End();
            throw;
        }

        IExplorerSession explorerSession;
        try
        {
            explorerSession = this._explorerAgent is IAutomationExplorerAgent automationAgent
                ? automationAgent.StartAutomationSession(
                    workspace!, openSessionPayload.ModelCallBudget, openSessionPayload.WrapUpWindow, openSessionPayload.SessionDirectory,
                    openSessionPayload.Capture)
                : this._explorerAgent.StartSession(workspace!);
        }
        catch
        {
            this._collector.End();
            throw;
        }

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
                    registration.LogFilePath,
                    registration.SessionDirectory));
        }
        catch
        {
            try
            {
                return await DisposeFailedOpenSessionAsync(explorerSession).ConfigureAwait(false);
            }
            finally
            {
                this._collector.End();
            }
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
        this._collector.End();

        var snapshot = this._collector.Snapshot();
        var status = snapshot.SummaryCrossCheck == "mismatched" ? "failed" : "closed";
        return AutomationResponseEnvelope.SuccessResponse(request.RequestId!, new CloseSessionResult(registration.SessionId, status, snapshot));
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

        if (IsTerminalOutcome(exchangeResult))
        {
            await this.RemoveAndDisposeSessionAsync(registration.SessionId).ConfigureAwait(false);
        }

        var snapshot = this._collector.Snapshot();
        var submitResponse = CreateSubmitResult(registration, exchangeResult, snapshot);
        if (snapshot.SummaryCrossCheck == "mismatched")
            submitResponse = submitResponse with
            {
                Outcome = "failed", SessionOpen = false, Failure = new SubmitFailure("MeasurementMismatch", "Conversation measurements disagree."),
            };

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
        this._collector.End();
    }

    private static SubmitResult CreateSubmitResult(
        AutomationSessionRegistration registration, AgentExchangeResult exchangeResult, SessionMeasurements measurements)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(exchangeResult);

        return exchangeResult switch
        {
            AgentReplyReceived replyReceived => CreateSubmitResult(
                registration,
                measurements,
                outcome: "reply_received",
                assistantText: replyReceived.ReplyText,
                assistantTextIsPartial: false,
                modelTurnsCompleted: replyReceived.ModelTurnsCompleted,
                reportedTokensConsumed: replyReceived.ReportedTokensConsumed,
                sessionOpen: true,
                budgetFailureReason: null,
                failure: null),

            AgentExchangeBudgetExceeded budgetExceeded => CreateSubmitResult(
                registration,
                measurements,
                outcome: "budget_exceeded",
                assistantText: budgetExceeded.PartialText,
                assistantTextIsPartial: !string.IsNullOrWhiteSpace(budgetExceeded.PartialText),
                modelTurnsCompleted: budgetExceeded.ModelTurnsCompleted,
                reportedTokensConsumed: null,
                sessionOpen: false,
                budgetFailureReason: budgetExceeded.Reason,
                failure: null),

            AgentExchangeMaxTurnsReached maxTurnsReached => CreateSubmitResult(
                registration,
                measurements,
                outcome: "max_turns_reached",
                assistantText: maxTurnsReached.PartialText,
                assistantTextIsPartial: !string.IsNullOrWhiteSpace(maxTurnsReached.PartialText),
                modelTurnsCompleted: maxTurnsReached.ModelTurnsCompleted,
                reportedTokensConsumed: null,
                sessionOpen: true,
                budgetFailureReason: null,
                failure: null),

            AgentExchangeCancelled cancelled => CreateSubmitResult(
                registration,
                measurements,
                outcome: "cancelled",
                assistantText: cancelled.PartialText,
                assistantTextIsPartial: !string.IsNullOrWhiteSpace(cancelled.PartialText),
                modelTurnsCompleted: cancelled.ModelTurnsCompleted,
                reportedTokensConsumed: null,
                sessionOpen: false,
                budgetFailureReason: null,
                failure: null),

            AgentExchangeTurnBudgetReached budgetReached => CreateSubmitResult(
                registration, measurements, "turn_budget_reached", budgetReached.PartialText,
                !string.IsNullOrWhiteSpace(budgetReached.PartialText), budgetReached.ModelTurnsCompleted, null, false, null, null),

            AgentExchangeFailed failed => CreateSubmitResult(
                registration,
                measurements,
                outcome: "failed",
                assistantText: null,
                assistantTextIsPartial: false,
                modelTurnsCompleted: failed.ModelTurnsCompleted,
                reportedTokensConsumed: null,
                sessionOpen: false,
                budgetFailureReason: null,
                failure: new SubmitFailure(
                    failed.Exception.GetType().FullName ?? failed.Exception.GetType().Name,
                    failed.Exception.Message)),

            _ => throw new InvalidOperationException($"Unsupported exchange result '{exchangeResult.GetType().Name}'.")
        };
    }

    private static SubmitResult CreateSubmitResult(
        AutomationSessionRegistration registration,
        SessionMeasurements measurements,
        string outcome,
        string? assistantText,
        bool assistantTextIsPartial,
        int modelTurnsCompleted,
        int? reportedTokensConsumed,
        bool sessionOpen,
        string? budgetFailureReason,
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
            budgetFailureReason,
            failure,
            measurements,
            measurements.ProviderCalls.Count,
            measurements.ModelCallBudget,
            Math.Max(0, measurements.ProviderCalls.Count - (measurements.ModelCallBudget ?? measurements.ProviderCalls.Count)));
    }

    private static bool IsTerminalOutcome(AgentExchangeResult exchangeResult)
    {
        return exchangeResult is AgentExchangeBudgetExceeded or AgentExchangeCancelled or AgentExchangeFailed or AgentExchangeTurnBudgetReached;
    }

    private sealed record AutomationPingResult(string Status, int ProtocolVersion, EffectiveSettings Settings);

    private sealed record OpenSessionResult(
        string SessionId,
        AutomationWorkspaceResult Workspace,
        string LogFilePath,
        string SessionDirectory);

    private sealed record OpenSessionPayload(
        string? WorkspacePath, string? RepositoryUrl, int? ModelCallBudget, int? WrapUpWindow, string? SessionDirectory, bool Capture = false,
        string? RepositoryCommit = null);

    private sealed record AutomationWorkspaceResult(
        string Name,
        string OwnerRepo,
        string LocalPath,
        DateTime ClonedAt,
        long SizeBytes);

    private sealed record CloseSessionResult(string SessionId, string Status, SessionMeasurements Measurements);

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
        string? BudgetFailureReason,
        SubmitFailure? Failure,
        SessionMeasurements Measurements,
        int ModelCallsMade,
        int? ModelCallBudget,
        int ModelCallBudgetOvershoot);

    private sealed record SubmitFailure(string ExceptionType, string Message);
}
