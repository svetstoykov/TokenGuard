using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace Codexplorer.Automation;

/// <summary>Processes commands sequentially while observing runner disconnection concurrently.</summary>
/// <remarks>Final responses and session disposal use tokens independent of cancelled model work.</remarks>
internal sealed class AutomationHost : IAsyncDisposable
{
    private readonly IAutomationProtocolChannel _channel;
    private readonly IAutomationCommandDispatcher _dispatcher;
    private readonly IAutomationSessionRegistry _sessionRegistry;
    private readonly ILogger<AutomationHost> _logger;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="AutomationHost" /> class.</summary>
    /// <param name="channel">The protocol input and output boundary.</param>
    /// <param name="dispatcher">The sequential command processor.</param>
    /// <param name="sessionRegistry">The active automation session registry.</param>
    /// <param name="logger">The diagnostic logger.</param>
    public AutomationHost(
        IAutomationProtocolChannel channel,
        IAutomationCommandDispatcher dispatcher,
        IAutomationSessionRegistry sessionRegistry,
        ILogger<AutomationHost> logger)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(sessionRegistry);
        ArgumentNullException.ThrowIfNull(logger);

        this._channel = channel;
        this._dispatcher = dispatcher;
        this._sessionRegistry = sessionRegistry;
        this._logger = logger;
    }

    /// <summary>Asynchronously processes commands until runner EOF or host cancellation.</summary>
    /// <param name="ct">The host cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the process exit code.</returns>
    public async Task<int> RunAsync(CancellationToken ct)
    {
        using var disconnected = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var readerLifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var input = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        var reader = this.ReadInputAsync(input.Writer, disconnected, readerLifetime.Token);
        try
        {
            await foreach (var line in input.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
            {
                var response = await this.ProcessLineAsync(line, disconnected.Token).ConfigureAwait(false);
                await this._channel.WriteResponseAsync(response, CancellationToken.None).ConfigureAwait(false);
                if (disconnected.IsCancellationRequested)
                    break;
            }

            return 0;
        }
        finally
        {
            try
            {
                await readerLifetime.CancelAsync().ConfigureAwait(false);
                await reader.ConfigureAwait(false);
            }
            finally
            {
                await this.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>Reads ahead so runner disconnection cancels the active command.</summary>
    /// <param name="writer">The queue used by the sequential command processor.</param>
    /// <param name="disconnected">The cancellation source shared with active work.</param>
    /// <param name="ct">The input reader lifetime token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private async Task ReadInputAsync(ChannelWriter<string> writer, CancellationTokenSource disconnected, CancellationToken ct)
    {
        try
        {
            while (await this._channel.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
                await writer.WriteAsync(line, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (IOException exception)
        {
            this._logger.LogWarning(exception, "Automation input disconnected.");
        }
        finally
        {
            await disconnected.CancelAsync().ConfigureAwait(false);
            writer.TryComplete();
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
            return AutomationResponseEnvelope.ErrorResponse(request?.RequestId, "cancelled", "Command cancelled.");
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

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (this._disposed)
        {
            return;
        }

        this._disposed = true;
        await this._sessionRegistry.DisposeAsync().ConfigureAwait(false);
        await this._channel.DisposeAsync().ConfigureAwait(false);
    }
}
