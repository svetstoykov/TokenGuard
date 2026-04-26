using Microsoft.Extensions.Logging;

namespace Codexplorer.Automation;

internal sealed class AutomationHost : IAsyncDisposable
{
    private readonly IAutomationProtocolChannel _channel;
    private readonly IAutomationCommandDispatcher _dispatcher;
    private readonly ILogger<AutomationHost> _logger;
    private bool _disposed;

    public AutomationHost(
        IAutomationProtocolChannel channel,
        IAutomationCommandDispatcher dispatcher,
        ILogger<AutomationHost> logger)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(logger);

        this._channel = channel;
        this._dispatcher = dispatcher;
        this._logger = logger;
    }

    public async Task<int> RunAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                string? line;

                try
                {
                    line = await this._channel.ReadLineAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }

                if (line is null)
                {
                    break;
                }

                var response = await this.ProcessLineAsync(line, ct).ConfigureAwait(false);

                try
                {
                    await this._channel.WriteResponseAsync(response, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
            }

            return 0;
        }
        finally
        {
            await this.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<AutomationResponseEnvelope> ProcessLineAsync(string line, CancellationToken ct)
    {
        AutomationRequestEnvelope? request = null;

        try
        {
            if (!AutomationProtocolJson.TryParseRequest(line, out request, out var errorResponse))
            {
                return errorResponse!;
            }

            return await this._dispatcher.DispatchAsync(request!, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Unhandled automation command failure for request {RequestId}", request?.RequestId);
            return AutomationResponseEnvelope.ErrorResponse(
                request?.RequestId,
                code: "internal_error",
                message: "Command processing failed.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (this._disposed)
        {
            return;
        }

        this._disposed = true;
        await this._channel.DisposeAsync().ConfigureAwait(false);
    }
}
