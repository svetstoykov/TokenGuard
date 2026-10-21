using System.Diagnostics;
using Codexplorer.Automation.Client;
using Codexplorer.Automation.Protocol;

namespace Codexplorer.Automation.AgentComparison.Codexplorer;

/// <summary>Runs the native Codexplorer protocol in an isolated child environment.</summary>
/// <remarks>One transport owns one process and permits one outstanding protocol request.</remarks>
internal sealed class ComparisonProtocolTransport(RuntimeInvocation invocation) : IAutomationProtocolTransport
{
    private Process? _process;
    private Task? _stderr;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <inheritdoc />
    public int? ProcessId => this._process?.Id;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (this._process is not null)
            return Task.CompletedTask;
        if (invocation.RecorderEndpoint.Scheme != "http" || !invocation.RecorderEndpoint.IsLoopback)
            throw new InvalidOperationException("Codexplorer comparison requires a loopback recorder.");
        ComparisonRepositorySafety.Validate(invocation.Checkout);
        Directory.CreateDirectory(invocation.StateRoot);
        var bundle = Path.Combine(invocation.StateRoot, "runtime");
        Directory.CreateDirectory(bundle);
        CopyBundle(Path.GetDirectoryName(invocation.Executable)!, bundle);
        var executable = Path.Combine(bundle, Path.GetFileName(invocation.Executable));
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = invocation.StateRoot,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add("--automation");
        start.Environment.Clear();
        start.Environment["PATH"] = OperatingSystem.IsWindows()
            ? Environment.GetEnvironmentVariable("SystemRoot") + "\\System32" : "/usr/local/share/dotnet:/usr/bin:/bin";
        foreach (var name in new[] { "DOTNET_ROOT", "SystemRoot", "WINDIR" })
            if (Environment.GetEnvironmentVariable(name) is { } value)
                start.Environment[name] = value;
        start.Environment["HOME"] = invocation.StateRoot;
        start.Environment["XDG_CONFIG_HOME"] = Path.Combine(invocation.StateRoot, "config");
        start.Environment["XDG_DATA_HOME"] = Path.Combine(invocation.StateRoot, "data");
        start.Environment["XDG_CACHE_HOME"] = Path.Combine(invocation.StateRoot, "cache");
        start.Environment["TMPDIR"] = invocation.StateRoot;
        start.Environment["DOTNET_ENVIRONMENT"] = "Production";
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["OPENROUTER_API_KEY"] = invocation.LocalCredential;
        start.Environment["CODEXPLORER_BENCHMARK_OPENROUTER_BASE_URL"] = invocation.RecorderEndpoint.AbsoluteUri;
        start.Environment["CODEXPLORER_BENCHMARK_WORKSPACE"] = invocation.Checkout;
        start.Environment["CODEXPLORER_BENCHMARK_SUMMARIZER_API_KEY"] = invocation.SummarizerCredential;
        start.Environment["Codexplorer__Model__Name"] = invocation.Settings.Model;
        start.Environment["Codexplorer__Model__MaxOutputTokens"] = invocation.Settings.MaxOutputTokens.ToString();
        start.Environment["Codexplorer__Budget__ContextWindowTokens"] = invocation.Session.ContextWindowTokens!.Value.ToString();
        start.Environment["Codexplorer__Workspace__RootDirectory"] = Path.GetDirectoryName(invocation.Checkout)!;
        start.Environment["Codexplorer__Logging__SessionLogsDirectory"] = Path.GetDirectoryName(invocation.Artifacts)!;
        this._process = Process.Start(start) ?? throw new IOException("Could not start isolated Codexplorer.");
        this._stderr = this.PumpErrorAsync(this._process.StandardError);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<AutomationResponseEnvelope> SendAsync(AutomationRequestEnvelope request, CancellationToken ct)
    {
        await this.StartAsync(ct).ConfigureAwait(false);
        await this._gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var process = this._process!;
            var line = AutomationProtocolJson.SerializeRequest(request);
            await File.AppendAllTextAsync(Path.Combine(invocation.StateRoot, "protocol-requests.jsonl"), line + "\n", ct);
            await process.StandardInput.WriteLineAsync(line.AsMemory(), ct).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(ct).ConfigureAwait(false);
            var responseLine = await process.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false);
            if (responseLine is null)
                throw new IOException("Codexplorer exited without a terminal protocol response.");
            await File.AppendAllTextAsync(Path.Combine(invocation.StateRoot, "protocol-responses.jsonl"), responseLine + "\n", ct);
            if (!AutomationProtocolJson.TryParseResponse(responseLine, out var response, out _)
                || response!.RequestId != request.RequestId)
                throw new InvalidDataException("Codexplorer returned an invalid protocol envelope.");
            return response;
        }
        finally
        {
            this._gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (this._process is { } process)
        {
            try
            {
                process.StandardInput.Close();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().ConfigureAwait(false);
            }
            if (this._stderr is not null)
                await this._stderr.ConfigureAwait(false);
            process.Dispose();
            this._process = null;
        }
        this._gate.Dispose();
    }

    private async Task PumpErrorAsync(StreamReader reader)
    {
        await using var output = new StreamWriter(Path.Combine(invocation.StateRoot, "runtime.stderr.log"));
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            await output.WriteLineAsync(line).ConfigureAwait(false);
    }

    private static void CopyBundle(string source, string destination)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            var name = Path.GetFileName(entry);
            if (name is "logs" or "appsettings.Development.json" || name.StartsWith('.'))
                continue;
            if (new FileInfo(entry).LinkTarget is not null || new DirectoryInfo(entry).LinkTarget is not null)
                throw new InvalidDataException("Runtime bundle links are forbidden.");
            var target = Path.Combine(destination, name);
            if (Directory.Exists(entry))
            {
                Directory.CreateDirectory(target);
                CopyBundle(entry, target);
            }
            else
            {
                File.Copy(entry, target, overwrite: true);
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(target, File.GetUnixFileMode(entry));
            }
        }
    }
}
