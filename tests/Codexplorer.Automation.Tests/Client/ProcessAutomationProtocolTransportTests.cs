using Codexplorer.Automation.Client;
using Codexplorer.Automation.Configuration;
using Codexplorer.Automation.Protocol;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Codexplorer.Automation.Tests.Client;

/// <summary>Verifies child-process cancellation and transport failures.</summary>
public sealed class ProcessAutomationProtocolTransportTests
{
    /// <summary>Verifies cancellation closes child input and retains its terminal response.</summary>
    [UnixFact]
    public async Task SendAsync_CancelledInFlight_ClosesStdinAndDrainsTerminalResponse()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var executable = Path.Combine(directory, "child.py");
        var ready = Path.Combine(directory, "ready");
        File.WriteAllText(executable, """
            #!/usr/bin/env python3
            import json, sys
            request = json.loads(sys.stdin.readline())
            open('ready', 'w').close()
            sys.stdin.read()
            result = {'requestId': request['requestId'], 'success': True,
                'result': {'outcome': 'cancelled', 'measurements': {'complete': True, 'modelCallsMade': 1}}}
            print(json.dumps(result), flush=True)
            """);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        try
        {
            await using var transport = new ProcessAutomationProtocolTransport(
                Options.Create(new CodexplorerAutomationOptions { CodexplorerExecutablePath = executable }),
                NullLogger<ProcessAutomationProtocolTransport>.Instance);
            using var cancellation = new CancellationTokenSource();
            var responseTask = transport.SendAsync(new AutomationRequestEnvelope("cancel-request", "submit", null), cancellation.Token);
            using var readyTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!File.Exists(ready))
            {
                await Task.Delay(10, readyTimeout.Token);
            }

            cancellation.Cancel();
            var response = await responseTask.WaitAsync(TimeSpan.FromSeconds(5));

            response.Success.Should().BeTrue();
            response.Result!.Value.GetProperty("outcome").GetString().Should().Be("cancelled");
            response.Result.Value.GetProperty("measurements").GetProperty("complete").GetBoolean().Should().BeTrue();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Verifies a closed child input pipe is a fatal transport failure.</summary>
    [UnixFact]
    public async Task SendAsync_ChildClosesStdinWhileAlive_ThrowsTransportFailure()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var executable = Path.Combine(directory, "closed-input.py");
        var ready = Path.Combine(directory, "ready");
        var release = Path.Combine(directory, "release");
        File.WriteAllText(executable, """
            #!/usr/bin/env python3
            import os, sys, time
            os.close(sys.stdin.fileno())
            open('ready', 'w').close()
            while not os.path.exists('release'):
                time.sleep(0.01)
            """);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        try
        {
            await using var transport = new ProcessAutomationProtocolTransport(
                Options.Create(new CodexplorerAutomationOptions { CodexplorerExecutablePath = executable }),
                NullLogger<ProcessAutomationProtocolTransport>.Instance);
            try
            {
                await transport.StartAsync(CancellationToken.None);
                using var readyTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                while (!File.Exists(ready))
                {
                    await Task.Delay(10, readyTimeout.Token);
                }

                var send = () => transport.SendAsync(new AutomationRequestEnvelope("broken-pipe", "submit", null), CancellationToken.None);

                await send.Should().ThrowAsync<CodexplorerAutomationTransportException>();
            }
            finally
            {
                File.WriteAllText(release, "");
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class UnixFactAttribute : FactAttribute
    {
        /// <summary>Initializes a new instance of the <see cref="UnixFactAttribute" /> class.</summary>
        public UnixFactAttribute()
        {
            if (OperatingSystem.IsWindows())
            {
                this.Skip = "The fake executable uses a Unix interpreter and executable mode.";
            }
        }
    }
}
