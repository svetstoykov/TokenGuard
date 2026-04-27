using System.Text.Json;
using Codexplorer.Automation.Protocol;

namespace Codexplorer.Automation.Client;

internal sealed class CodexplorerAutomationClient : ICodexplorerAutomationClient
{
    private readonly IAutomationProtocolTransport _transport;
    private long _requestSequence;

    public CodexplorerAutomationClient(IAutomationProtocolTransport transport)
    {
        ArgumentNullException.ThrowIfNull(transport);
        this._transport = transport;
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
            return typedResult
                ?? throw new CodexplorerAutomationTransportException(
                    $"Codexplorer returned an empty result payload for request '{requestId}'.");
        }
        catch (JsonException ex)
        {
            throw new CodexplorerAutomationTransportException(
                $"Failed to deserialize '{command}' result for request '{requestId}'.",
                ex);
        }
    }
}
