extern alias sample;

using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;

namespace Codexplorer.Automation.Tests;

/// <summary>Verifies the sample protocol over real redirected console streams without provider calls.</summary>
public sealed class SampleConsoleProtocolTests
{
    /// <summary>Verifies ping replies while input stays open and EOF shuts the child down.</summary>
    [Fact]
    public async Task Ping_WithStdinOpen_RespondsBeforeTheNextCommand()
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(typeof(sample::Codexplorer.Program).Assembly.Location);
        start.ArgumentList.Add("--automation");
        start.Environment["OPENROUTER_API_KEY"] = "unused-offline-test-key";
        using var child = Process.Start(start)!;
        var diagnostics = child.StandardError.ReadToEndAsync();
        try
        {
            await child.StandardInput.WriteLineAsync("""{"requestId":"ping-test","command":"ping"}""");
            await child.StandardInput.FlushAsync();

            var line = await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));

            line.Should().NotBeNull();
            using var response = JsonDocument.Parse(line!);
            response.RootElement.GetProperty("requestId").GetString().Should().Be("ping-test");
            response.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
            child.StandardInput.Close();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            child.ExitCode.Should().Be(0);
        }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync();
            }
            await diagnostics;
        }
    }
}
