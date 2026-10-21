using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Codexplorer.Automation.AgentComparison;
using Codexplorer.Automation.AgentComparison.Recording;
using FluentAssertions;

namespace Codexplorer.Automation.Tests.AgentComparison.Recording;

/// <summary>Verifies capture failures cannot certify a complete billed population.</summary>
public sealed class RecordingCaptureFailureTests
{
    /// <summary>Retains a billed response in memory and prevents more forwarding after capture storage fails.</summary>
    [Fact]
    public async Task Request_CaptureStorageFails_RetainsIncompleteAttemptAndRefusesFurtherForwarding()
    {
        var root = Path.Combine(Path.GetTempPath(), "capture-failure-" + Guid.NewGuid().ToString("N"));
        var capture = Path.Combine(root, "capture");
        Directory.CreateDirectory(root);
        try
        {
            var usage = JsonSerializer.SerializeToElement(new { prompt_tokens = 100, completion_tokens = 20, cost = 0.1m });
            var provider = new FakeOpenRouterProvider([new FakeCompletion("synthetic", [], usage, "capture-failed")]);
            await using var proxy = new RecordingProxy(new RecordingProxyOptions(RecordingProxyMode.Fake, null, null, provider,
                capture, new ComparisonSettings("qwen/qwen3.7-flash", "alibaba", 32768, true)));
            await proxy.StartAsync();
            var scope = proxy.BeginScope(new RecordingScopeOptions("capture-failed", 4, 1, "local-capture"));
            Directory.Delete(capture);
            await File.WriteAllTextAsync(capture, "Storage blocked by this synthetic fixture.");
            using var client = new HttpClient();
            using var first = await SendAsync(client, scope);
            await proxy.DrainAsync();
            first.StatusCode.Should().Be(HttpStatusCode.OK);
            proxy.CaptureFailure.Should().NotBeNullOrWhiteSpace();
            proxy.Attempts.Should().ContainSingle();
            var totals = ComparisonAccounting.Aggregate(proxy.Attempts);
            totals.CompleteTokens.Should().BeFalse();
            totals.CompleteCost.Should().BeFalse();
            totals.KnownCost.Should().Be(0.1m);
            using var second = await SendAsync(client, scope);
            await proxy.DrainAsync();
            second.StatusCode.Should().Be(HttpStatusCode.BadGateway);
            provider.Requests.Should().HaveCount(1, "failed capture must stop further upstream attempts");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, IRecordingScope scope)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(scope.Endpoint, "chat/completions"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", scope.Credential);
        request.Content = new StringContent("""
            {"model":"synthetic","messages":[{"role":"user","content":"fixture"}]}
            """, Encoding.UTF8, "application/json");
        return client.SendAsync(request);
    }
}
