using Codexplorer.Automation.Client;
using Codexplorer.Automation.Configuration;
using Codexplorer.Automation.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Codexplorer.Automation;

internal sealed class AutomationRunner
{
    private readonly IAutomationProtocolTransport _transport;
    private readonly ICodexplorerAutomationClient _client;
    private readonly CodexplorerAutomationOptions _options;
    private readonly ILogger<AutomationRunner> _logger;

    public AutomationRunner(
        IAutomationProtocolTransport transport,
        ICodexplorerAutomationClient client,
        IOptions<CodexplorerAutomationOptions> options,
        ILogger<AutomationRunner> logger)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        this._transport = transport;
        this._client = client;
        this._options = options.Value;
        this._logger = logger;
    }

    public async Task<int> RunAsync(CancellationToken ct)
    {
        try
        {
            await this._transport.StartAsync(ct).ConfigureAwait(false);

            var ping = await this._client.PingAsync(ct).ConfigureAwait(false);
            this._logger.LogInformation(
                "Connected to Codexplorer automation protocol v{ProtocolVersion} using process {ProcessId}.",
                ping.ProtocolVersion,
                this._transport.ProcessId);

            foreach (var configuredWorkspacePath in this._options.WorkspacePaths)
            {
                var resolvedWorkspacePath = AutomationPathResolver.ResolveFromCurrentDirectory(configuredWorkspacePath);
                var openedSession = await this._client
                    .OpenSessionAsync(new OpenSessionRequest(resolvedWorkspacePath), ct)
                    .ConfigureAwait(false);

                this._logger.LogInformation(
                    "Opened automation session {SessionId} for workspace {WorkspacePath}.",
                    openedSession.SessionId,
                    openedSession.Workspace.LocalPath);

                var closedSession = await this._client
                    .CloseSessionAsync(new CloseSessionRequest(openedSession.SessionId), ct)
                    .ConfigureAwait(false);

                this._logger.LogInformation(
                    "Closed automation session {SessionId} with status {Status}.",
                    closedSession.SessionId,
                    closedSession.Status);
            }

            return 0;
        }
        catch (CodexplorerAutomationProtocolException ex)
        {
            this._logger.LogError(
                ex,
                "Codexplorer returned protocol error {ErrorCode} for request {RequestId}.",
                ex.Code,
                ex.RequestId);
            return 1;
        }
        catch (CodexplorerProcessExitedException ex)
        {
            this._logger.LogError(
                ex,
                "Codexplorer process exited unexpectedly with code {ExitCode}.",
                ex.ExitCode);
            return 1;
        }
        catch (CodexplorerAutomationTransportException ex)
        {
            this._logger.LogError(ex, "Codexplorer automation transport failed.");
            return 1;
        }
    }
}
