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
            return this.OpenSessionAsync(request);
        }

        if (string.Equals(request.Command, "close_session", StringComparison.OrdinalIgnoreCase))
        {
            return this.CloseSessionAsync(request);
        }

        if (string.Equals(request.Command, "submit", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(
                AutomationResponseEnvelope.ErrorResponse(
                    request.RequestId,
                    code: "not_implemented",
                    message: "Command 'submit' is recognized but not implemented yet."));
        }

        return Task.FromResult(
            AutomationResponseEnvelope.ErrorResponse(
                request.RequestId,
                code: "unknown_command",
                message: $"Command '{request.Command}' is not supported."));
    }

    private Task<AutomationResponseEnvelope> OpenSessionAsync(AutomationRequestEnvelope request)
    {
        if (!TryGetRequiredStringPayloadProperty(request, "workspacePath", out var workspacePath, out var errorResponse))
        {
            return Task.FromResult(errorResponse!);
        }

        Codexplorer.Workspace.Workspace? workspace;

        try
        {
            workspace = this._workspaceManager.FindByLocalPath(workspacePath!);
        }
        catch (ArgumentException ex)
        {
            return Task.FromResult(
                AutomationResponseEnvelope.ErrorResponse(
                    request.RequestId,
                    code: "invalid_request",
                    message: ex.Message));
        }

        if (workspace is null)
        {
            return Task.FromResult(
                AutomationResponseEnvelope.ErrorResponse(
                    request.RequestId,
                    code: "workspace_not_found",
                    message: $"No tracked workspace exists at '{Path.GetFullPath(workspacePath!)}'."));
        }

        var explorerSession = this._explorerAgent.StartSession(workspace);

        try
        {
            var registration = this._sessionRegistry.Add(workspace, explorerSession);
            return Task.FromResult(
                AutomationResponseEnvelope.SuccessResponse(
                    request.RequestId!,
                    new OpenSessionResult(
                        registration.SessionId,
                        new AutomationWorkspaceResult(
                            workspace.Name,
                            workspace.OwnerRepo,
                            workspace.LocalPath,
                            workspace.ClonedAt,
                            workspace.SizeBytes),
                        registration.LogFilePath)));
        }
        catch
        {
            return DisposeFailedOpenSessionAsync(explorerSession);
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

    private static async Task<AutomationResponseEnvelope> DisposeFailedOpenSessionAsync(IExplorerSession explorerSession)
    {
        await explorerSession.DisposeAsync().ConfigureAwait(false);
        throw new InvalidOperationException("Failed to register opened automation session.");
    }

    private sealed record AutomationPingResult(string Status, int ProtocolVersion);

    private sealed record OpenSessionResult(
        string SessionId,
        AutomationWorkspaceResult Workspace,
        string LogFilePath);

    private sealed record AutomationWorkspaceResult(
        string Name,
        string OwnerRepo,
        string LocalPath,
        DateTime ClonedAt,
        long SizeBytes);

    private sealed record CloseSessionResult(string SessionId, string Status);
}
