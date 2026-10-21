using System.Text.Json;

namespace Codexplorer.Automation.AgentComparison.OpenCode;

/// <summary>
/// Captures pinned OpenCode events for one session. OpenCode v1.19.3 emits the legacy
/// <c>session.compacted</c> event; the newer <c>session.next.compaction.started/ended</c>
/// names are not part of this pinned runtime's event contract.
/// </summary>
internal sealed class OpenCodeEventCapture : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _pump;
    private readonly string _sessionId;

    private OpenCodeEventCapture(HttpClient client, string sessionId, string checkout, string path)
    {
        this._sessionId = sessionId;
        this._pump = this.PumpAsync(client, checkout, path, this._stop.Token);
    }

    /// <summary>Starts capturing events for the given OpenCode session.</summary>
    /// <param name="client">The HTTP client connected to the local OpenCode server.</param>
    /// <param name="sessionId">The OpenCode session identifier to capture.</param>
    /// <param name="checkout">The checkout directory used to scope the event stream.</param>
    /// <param name="path">The JSONL file that receives matching events.</param>
    /// <returns>A capture that can be disposed after the session ends.</returns>
    public static OpenCodeEventCapture Start(
        HttpClient client,
        string sessionId,
        string checkout,
        string path)
    {
        return new OpenCodeEventCapture(client, sessionId, checkout, path);
    }

    private async Task CompleteAsync(CancellationToken cancellationToken)
    {
        this._stop.Cancel();
        try
        {
            await this._pump.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or HttpRequestException or IOException
            or TimeoutException)
        {
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await this.CompleteAsync(CancellationToken.None).ConfigureAwait(false);
        this._stop.Dispose();
    }

    private async Task PumpAsync(
        HttpClient client,
        string checkout,
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"/event?directory={Uri.EscapeDataString(checkout)}");
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, useAsync: true);
            using var reader = new StreamReader(source);
            await using var writer = new StreamWriter(target);
            string? data = null;
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                    break;
                if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    data = line[5..].TrimStart();
                    continue;
                }
                if (line.Length == 0 && data is not null)
                {
                    await WriteEventAsync(writer, data, cancellationToken).ConfigureAwait(false);
                    data = null;
                }
            }

            if (data is not null)
                await WriteEventAsync(writer, data, cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or HttpRequestException or IOException
            or InvalidDataException or JsonException)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                await File.AppendAllTextAsync(
                    path,
                    JsonSerializer.Serialize(new { type = "capture.error", message = exception.GetType().Name }) + Environment.NewLine,
                    CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private async Task WriteEventAsync(StreamWriter writer, string data, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(data);
        var matchesSession = document.RootElement.TryGetProperty("properties", out var properties)
            && properties.TryGetProperty("sessionID", out var sessionIdElement)
            && sessionIdElement.ValueKind == JsonValueKind.String
            && sessionIdElement.GetString() == this._sessionId;
        if (!matchesSession)
            return;
        await writer.WriteLineAsync(data.AsMemory(), cancellationToken).ConfigureAwait(false);
    }
}
