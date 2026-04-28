using System.Text.Json;
using Codexplorer.Automation.Protocol;
using Microsoft.Extensions.Logging;

namespace Codexplorer.Automation.Client;

internal sealed class CodexplorerAutomationClient : ICodexplorerAutomationClient
{
    private readonly IAutomationProtocolTransport _transport;
    private readonly ILogger<CodexplorerAutomationClient> _logger;
    private long _requestSequence;

    public CodexplorerAutomationClient(
        IAutomationProtocolTransport transport,
        ILogger<CodexplorerAutomationClient> logger)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(logger);
        this._transport = transport;
        this._logger = logger;
    }

    public Task<AutomationPingResult> PingAsync(CancellationToken ct)
    {
        return this.SendCommandAsync<object?, AutomationPingResult>("ping", payload: null, ct);
    }

    public Task<OpenSessionResponse> OpenSessionAsync(OpenSessionRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        return this.SendCommandAsync<OpenSessionRequest, OpenSessionResponse>("open_session", request, ct);
    }

    public Task<SubmitResponse> SubmitAsync(SubmitRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        return this.SendCommandAsync<SubmitRequest, SubmitResponse>("submit", request, ct);
    }

    public Task<CloseSessionResponse> CloseSessionAsync(CloseSessionRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        return this.SendCommandAsync<CloseSessionRequest, CloseSessionResponse>("close_session", request, ct);
    }

    private async Task<TResult> SendCommandAsync<TPayload, TResult>(string command, TPayload payload, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        var requestId = $"runner-{Interlocked.Increment(ref this._requestSequence):D6}";
        this._logger.LogInformation(
            "Sending protocol command {Command} with request {RequestId}.",
            command,
            requestId);
        var response = await this._transport
            .SendAsync(new AutomationRequestEnvelope(requestId, command, payload), ct)
            .ConfigureAwait(false);

        if (!response.Success)
        {
            var error = response.Error
                ?? throw new CodexplorerAutomationTransportException(
                    $"Codexplorer returned an unsuccessful response without an error payload for request '{requestId}'.");

            throw new CodexplorerAutomationProtocolException(response.RequestId, error.Code, error.Message);
        }

        if (response.Result is null)
        {
            throw new CodexplorerAutomationTransportException(
                $"Codexplorer returned a successful response without a result payload for request '{requestId}'.");
        }

        try
        {
            var typedResult = response.Result.Value.Deserialize<TResult>(AutomationProtocolJson.SerializerOptions);
            if (typedResult is null)
            {
                throw new CodexplorerAutomationTransportException(
                    $"Codexplorer returned an empty result payload for request '{requestId}'.");
            }

            this._logger.LogInformation(
                "Protocol command {Command} with request {RequestId} completed successfully.",
                command,
                requestId);
            return typedResult;
        }
        catch (JsonException ex)
        {
            throw new CodexplorerAutomationTransportException(
                $"Failed to deserialize '{command}' result for request '{requestId}'.",
                ex);
        }
    }
}
