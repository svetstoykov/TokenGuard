namespace Codexplorer.ConsoleShell;

internal sealed class CancellationCoordinator : IDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _appCancellationSource = new();
    private CancellationTokenSource? _activeRunCancellationSource;
    private bool _disposed;

    public CancellationCoordinator()
    {
        Console.CancelKeyPress += this.OnCancelKeyPress;
        AppDomain.CurrentDomain.ProcessExit += this.OnProcessExit;
    }

    public CancellationToken AppCancellationToken => this._appCancellationSource.Token;

    public CancellationTokenSource BeginAgentRun()
    {
        var runCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(this._appCancellationSource.Token);

        lock (this._gate)
        {
            ObjectDisposedException.ThrowIf(this._disposed, this);

            if (this._activeRunCancellationSource is not null)
            {
                runCancellationSource.Dispose();
                throw new InvalidOperationException("Only one active agent run is supported at a time.");
            }

            this._activeRunCancellationSource = runCancellationSource;
            return runCancellationSource;
        }
    }

    public void EndAgentRun(CancellationTokenSource runCancellationSource)
    {
        ArgumentNullException.ThrowIfNull(runCancellationSource);

        lock (this._gate)
        {
            if (ReferenceEquals(this._activeRunCancellationSource, runCancellationSource))
            {
                this._activeRunCancellationSource = null;
            }
        }

        runCancellationSource.Dispose();
    }

    public void Dispose()
    {
        lock (this._gate)
        {
            if (this._disposed)
            {
                return;
            }

            this._disposed = true;
            Console.CancelKeyPress -= this.OnCancelKeyPress;
            AppDomain.CurrentDomain.ProcessExit -= this.OnProcessExit;
            this._activeRunCancellationSource?.Cancel();
            this._activeRunCancellationSource?.Dispose();
            this._activeRunCancellationSource = null;
            this._appCancellationSource.Cancel();
            this._appCancellationSource.Dispose();
        }
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        CancellationTokenSource? activeRunCancellationSource;

        lock (this._gate)
        {
            activeRunCancellationSource = this._activeRunCancellationSource;
        }

        if (activeRunCancellationSource is not null && !activeRunCancellationSource.IsCancellationRequested)
        {
            e.Cancel = true;
            activeRunCancellationSource.Cancel();
            return;
        }

        try
        {
            this._appCancellationSource.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        try
        {
            this._appCancellationSource.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
