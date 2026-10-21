using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Codexplorer.Automation.AgentComparison;
using Codexplorer.Automation.AgentComparison.Recording;
using FluentAssertions;

namespace Codexplorer.Automation.Tests.AgentComparison.Preparation;

/// <summary>Verifies preparation and stale-plan refusal with an intercepted public metadata boundary.</summary>
public sealed class LaunchPreparationBoundaryTests
{
    /// <summary>Creates only redacted setup/readiness artifacts when credentials and runtimes are absent.</summary>
    [Fact]
    public async Task PrepareAndPreflight_AbsentCredentialsAndStalePlan_NeverInvokeAnInferencePath()
    {
        var root = Path.Combine(AgentComparisonCommand.FindRepositoryRoot(), ".artifacts", "reports", "opencode-comparison",
            "scratch", "verification", "preparation-fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = AgentComparisonCommand.FindRepositoryRoot();
            var tasks = Path.Combine(root, "samples", "Codexplorer.Automation", "src", "tasks");
            Directory.CreateDirectory(tasks);
            foreach (var tier in new[] { "100k", "200k", "300k" })
            {
                var name = $"long-sessions-{tier}.json";
                File.Copy(Path.Combine(source, "samples", "Codexplorer.Automation", "src", "tasks", name), Path.Combine(tasks, name));
            }
            await File.WriteAllTextAsync(Path.Combine(root, ".gitignore"), ".artifacts/\n");
            await File.WriteAllTextAsync(Path.Combine(root, "TokenGuard.sln"), "Synthetic preparation fixture.");
            await GitAsync(root, ["init", "-b", "main", "--quiet"]);
            await GitAsync(root, ["add", "."]);
            await GitAsync(root, ["-c", "user.name=Fixture", "-c", "user.email=fixture@invalid", "commit", "--quiet", "-m", "fixture"]);
            var handler = new CatalogHandler();
            using var client = new HttpClient(handler);
            IComparisonCredentialResolver credentials = new ComparisonCredentialResolver(() => null, Path.Combine(root, "missing.json"));
            ILaunchPreparation preparation = new LaunchPreparation(credentials, client);
            var output = Path.Combine(root, ".artifacts", "reports", "opencode-comparison");
            var plan = await preparation.PrepareAsync(root, output, 1, CancellationToken.None);
            var planPath = Path.Combine(output, "scratch", "launch-plan.json");
            var readiness = await preparation.PreflightAsync(planPath, CancellationToken.None);
            plan.Sessions.Should().HaveCount(18);
            plan.LiveExecutionStarted.Should().BeFalse();
            readiness.CredentialPresent.Should().BeFalse();
            readiness.LiveExecutionStarted.Should().BeFalse();
            readiness.Ready.Should().BeFalse();
            readiness.Blockers.Should().Contain(blocker => blocker.Contains("credential", StringComparison.OrdinalIgnoreCase));
            handler.Requests.Should().Equal("https://openrouter.ai/api/v1/models/qwen/qwen3.7-flash/endpoints");
            var tampered = plan with { Settings = plan.Settings with { MaxOutputTokens = 1 } };
            await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(tampered, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            var stale = await preparation.PreflightAsync(planPath, CancellationToken.None);
            stale.Ready.Should().BeFalse();
            stale.LiveExecutionStarted.Should().BeFalse();
            stale.Blockers.Any(blocker => blocker.Contains("digest", StringComparison.OrdinalIgnoreCase)).Should().BeTrue();
            handler.Requests.Should().HaveCount(1, "preflight uses bound metadata and never creates an inference path");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task GitAsync(string root, string[] args)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
        };
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_CONFIG_GLOBAL"] = "/dev/null";
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await stdout;
        var error = await stderr;
        if (process.ExitCode != 0)
            throw new IOException("Local preparation fixture failed: " + error);
    }

    private sealed class CatalogHandler : HttpMessageHandler
    {
        internal List<string> Requests { get; } = [];

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.Requests.Add(request.RequestUri!.AbsoluteUri);
            var body = """
                {"data":{"endpoints":[{"tag":"alibaba","status":0,"context_length":1000000,"max_completion_tokens":32768}]}}
                """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
