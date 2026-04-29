namespace Codexplorer.Automation;

internal sealed class ConsoleAutomationProtocolChannel : IAutomationProtocolChannel
{
    private readonly TextReader _reader;
    private readonly TextWriter _writer;
    private readonly bool _disposeStreams;
    private bool _disposed;

    public ConsoleAutomationProtocolChannel()
        : this(Console.In, Console.Out, disposeStreams: false)
    {
    }

    internal ConsoleAutomationProtocolChannel(
        TextReader reader,
        TextWriter writer,
        bool disposeStreams)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(writer);

        this._reader = reader;
        this._writer = writer;
        this._disposeStreams = disposeStreams;
    }

    public ValueTask<string?> ReadLineAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(this._disposed, this);
        return this._reader.ReadLineAsync(ct);
    }

    public async ValueTask WriteResponseAsync(AutomationResponseEnvelope response, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(this._disposed, this);

        var line = AutomationProtocolJson.SerializeResponse(response);
        await this._writer.WriteLineAsync(line.AsMemory(), ct).ConfigureAwait(false);
        await this._writer.FlushAsync(ct).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        if (this._disposed)
        {
            return ValueTask.CompletedTask;
        }

        this._disposed = true;

        if (!this._disposeStreams)
        {
            return ValueTask.CompletedTask;
        }

        this._reader.Dispose();
        this._writer.Dispose();
        return ValueTask.CompletedTask;
    }
}
