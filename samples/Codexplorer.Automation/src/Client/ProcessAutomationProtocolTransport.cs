using System.Collections.Concurrent;
using System.Diagnostics;
using Codexplorer.Automation.Configuration;
using Codexplorer.Automation.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Codexplorer.Automation.Client;

/// <summary>Exchanges JSON-lines commands with a child process and bounds cancellation cleanup.</summary>
internal sealed class ProcessAutomationProtocolTransport : IAutomationProtocolTransport
{
    private const int StandardErrorHistoryLimit = 200;
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(5);

    private readonly CodexplorerAutomationOptions _options;
    private readonly ILogger<ProcessAutomationProtocolTransport> _logger;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly ConcurrentQueue<string> _standardErrorLines = new();
    private readonly Lock _sync = new();

    private Process? _process;
    private StreamWriter? _standardInput;
    private StreamReader? _standardOutput;
    private Task? _standardErrorPump;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="ProcessAutomationProtocolTransport" /> class.</summary>
    /// <param name="options">The child process configuration.</param>
    /// <param name="logger">The transport logger.</param>
    public ProcessAutomationProtocolTransport(
        IOptions<CodexplorerAutomationOptions> options,
        ILogger<ProcessAutomationProtocolTransport> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        this._options = options.Value;
        this._logger = logger;
    }

    /// <inheritdoc />
    public int? ProcessId => this._process?.Id;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(this._disposed, this);
        ct.ThrowIfCancellationRequested();

        lock (this._sync)
        {
            if (this._process is { HasExited: false })
            {
                return Task.CompletedTask;
            }

            if (this._process is { HasExited: true })
            {
                throw this.CreateExitedException("Codexplorer exited before the automation transport could start.");
            }

            var executablePath = this._options.CodexplorerExecutablePath;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                throw new CodexplorerAutomationTransportException("Codexplorer executable path is not configured.");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("--automation");
            if (this._options.Arm == "control")
            {
                startInfo.Environment["Codexplorer__Budget__ContextWindowTokens"] =
                    this._options.ControlContextWindowTokens.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };

            if (!process.Start())
            {
                throw new CodexplorerAutomationTransportException(
                    $"Failed to start Codexplorer executable at '{executablePath}'.");
            }

            this._logger.LogInformation(
                "Started Codexplorer automation process {ProcessId} from {ExecutablePath}.",
                process.Id,
                executablePath);

            this._process = process;
            this._standardInput = process.StandardInput;
            this._standardOutput = process.StandardOutput;
            this._standardErrorPump = Task.Run(
                () => this.PumpStandardErrorAsync(process.StandardError),
                CancellationToken.None);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<AutomationResponseEnvelope> SendAsync(AutomationRequestEnvelope request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        await this.StartAsync(ct).ConfigureAwait(false);
        await this._requestGate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            var process = this._process ?? throw new CodexplorerAutomationTransportException("Codexplorer process is not available.");

            if (process.HasExited)
            {
                throw this.CreateExitedException("Codexplorer exited before the request could be sent.");
            }

            var standardInput = this._standardInput
                ?? throw new CodexplorerAutomationTransportException("Codexplorer stdin is not available.");
            var standardOutput = this._standardOutput
                ?? throw new CodexplorerAutomationTransportException("Codexplorer stdout is not available.");

            var requestLine = AutomationProtocolJson.SerializeRequest(request);
            this._logger.LogInformation(
                "Dispatching protocol request {RequestId} ({Command}) to Codexplorer process {ProcessId}.",
                request.RequestId,
                request.Command,
                process.Id);
            Task<string?>? readTask = null;
            string? responseLine;
            try
            {
                await standardInput.WriteLineAsync(requestLine.AsMemory(), ct).ConfigureAwait(false);
                await standardInput.FlushAsync(ct).ConfigureAwait(false);
                readTask = standardOutput.ReadLineAsync(CancellationToken.None).AsTask();
                responseLine = await readTask.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                standardInput.Close();
                readTask ??= standardOutput.ReadLineAsync(CancellationToken.None).AsTask();
                try
                {
                    responseLine = await readTask.WaitAsync(CleanupTimeout).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is TimeoutException or IOException or ObjectDisposedException)
                {
                    TerminateProcess(process);
                    throw new OperationCanceledException("Codexplorer did not return a terminal snapshot before cleanup expired.", ex, ct);
                }
            }
            if (responseLine is null)
            {
                throw this.CreateExitedException("Codexplorer closed stdout before returning a protocol response.");
            }

            if (!AutomationProtocolJson.TryParseResponse(responseLine, out var response, out var parseError))
            {
                throw new CodexplorerAutomationTransportException(parseError!);
            }

            if (!string.Equals(response!.RequestId, request.RequestId, StringComparison.Ordinal))
            {
                throw new CodexplorerAutomationTransportException(
                    $"Codexplorer returned response '{response.RequestId}' for request '{request.RequestId}'.");
            }

            this._logger.LogInformation(
                "Received protocol response {RequestId} ({Command}) from Codexplorer process {ProcessId}. Success={Success}.",
                response.RequestId,
                request.Command,
                process.Id,
                response.Success);
            return response;
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            if (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException("Codexplorer protocol work was cancelled.", exception, ct);
            }

            throw new CodexplorerAutomationTransportException("Codexplorer protocol stream failed.", exception);
        }
        finally
        {
            this._requestGate.Release();
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

        Process? process;
        StreamWriter? standardInput;
        Task? standardErrorPump;

        lock (this._sync)
        {
            process = this._process;
            standardInput = this._standardInput;
            standardErrorPump = this._standardErrorPump;

            this._process = null;
            this._standardInput = null;
            this._standardOutput = null;
            this._standardErrorPump = null;
        }

        try
        {
            standardInput?.Dispose();
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
        }

        if (process is not null)
        {
            try
            {
                if (!process.HasExited)
                {
                    this._logger.LogInformation(
                        "Waiting for Codexplorer automation process {ProcessId} to exit during transport disposal.",
                        process.Id);
                    try
                    {
                        await process.WaitForExitAsync().WaitAsync(CleanupTimeout).ConfigureAwait(false);
                    }
                    catch (TimeoutException)
                    {
                        TerminateProcess(process);
                        await process.WaitForExitAsync().WaitAsync(CleanupTimeout).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                this._logger.LogInformation(
                    "Disposed Codexplorer automation process {ProcessId} with exit code {ExitCode}.",
                    process.Id,
                    process.HasExited ? process.ExitCode : null);
                process.Dispose();
            }
        }

        if (standardErrorPump is not null)
        {
            try
            {
                await standardErrorPump.WaitAsync(CleanupTimeout).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is TimeoutException or ObjectDisposedException or IOException)
            {
                this._logger.LogWarning("Codexplorer stderr cleanup did not complete normally.");
            }
        }

        this._requestGate.Dispose();
    }

    private async Task PumpStandardErrorAsync(StreamReader standardError)
    {
        while (true)
        {
            var line = await standardError.ReadLineAsync().ConfigureAwait(false);
            if (line is null)
            {
                return;
            }

            this._standardErrorLines.Enqueue(line);
            while (this._standardErrorLines.Count > StandardErrorHistoryLimit)
            {
                this._standardErrorLines.TryDequeue(out _);
            }

            this._logger.LogInformation("Codexplorer stderr: {StandardErrorLine}", line);
        }
    }

    private static void TerminateProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private CodexplorerProcessExitedException CreateExitedException(string message)
    {
        int? exitCode = this._process?.HasExited == true ? this._process.ExitCode : null;
        var standardError = string.Join(Environment.NewLine, this._standardErrorLines);

        if (!string.IsNullOrWhiteSpace(standardError))
        {
            message = $"{message}{Environment.NewLine}{Environment.NewLine}Recent stderr:{Environment.NewLine}{standardError}";
        }

        return new CodexplorerProcessExitedException(message, exitCode, standardError);
    }
}
