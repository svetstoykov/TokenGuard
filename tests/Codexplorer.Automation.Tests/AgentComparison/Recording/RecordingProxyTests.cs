using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Codexplorer.Automation.AgentComparison;
using Codexplorer.Automation.AgentComparison.Recording;
using FluentAssertions;

namespace Codexplorer.Automation.Tests.AgentComparison.Recording;

/// <summary>Verifies the comparison boundary and its observable outcomes.</summary>
public sealed class RecordingProxyTests
{
    /// <summary>Verifies requests enforce main and reserved wrap up budgets and exclude credential from capture.</summary>
    [Fact]
    public async Task Requests_EnforceMainAndReservedWrapUpBudgets_AndExcludeCredentialFromCapture()
    {
        var captureDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var secret = "disposable-test-secret";
        var provider = new FakeOpenRouterProvider(
            [ToolCompletion("one", "read_file"), ToolCompletion("two", "grep"), Completion("wrap")]);
        await using var proxy = new RecordingProxy(new RecordingProxyOptions(
            RecordingProxyMode.Fake, null, null, provider, captureDirectory,
            new ComparisonSettings("qwen/qwen3.7-flash", "alibaba", 32768, true)));
        await proxy.StartAsync();
        var scope = proxy.BeginScope(new RecordingScopeOptions("budget-session", 3, 1, secret));
        using var client = new HttpClient();

        var first = await SendAsync(client, scope, "first");
        var second = await SendAsync(client, scope, "second");
        var rejectedMain = await SendAsync(client, scope, "third");
        scope.BeginWrapUp();
        var wrapUp = await SendAsync(client, scope, "wrap");
        var rejectedWrapUp = await SendAsync(client, scope, "extra");
        await WaitForAttemptsAsync(proxy, 5);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        rejectedMain.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        wrapUp.StatusCode.Should().Be(HttpStatusCode.OK);
        rejectedWrapUp.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        scope.MainCallsConsumed.Should().Be(2);
        scope.ConsumedWrapUpCalls.Should().Be(1);
        scope.MainBudgetExhausted.IsCancellationRequested.Should().BeTrue();
        proxy.Attempts.Should().HaveCount(5);
        proxy.Attempts.Count(attempt => attempt.BudgetRejected).Should().Be(2);
        proxy.Attempts.Select(attempt => attempt.Sequence).Should().OnlyHaveUniqueItems();
        provider.Requests.Should().HaveCount(3);
        foreach (var capturedFile in Directory.EnumerateFiles(captureDirectory, "*", SearchOption.AllDirectories))
            (await File.ReadAllTextAsync(capturedFile)).Should().NotContain(secret);
        proxy.Attempts.Should().OnlyContain(attempt => attempt.SessionId == "budget-session");
        using var effective = JsonDocument.Parse(proxy.Attempts[0].EffectiveBody);
        effective.RootElement.GetProperty("model").GetString().Should().Be("qwen/qwen3.7-flash");
        effective.RootElement.GetProperty("max_tokens").GetInt32().Should().Be(32768);
        effective.RootElement.GetProperty("reasoning").GetProperty("enabled").GetBoolean().Should().BeTrue();
        effective.RootElement.GetProperty("provider").GetProperty("allow_fallbacks").GetBoolean().Should().BeFalse();
        effective.RootElement.GetProperty("transforms").GetArrayLength().Should().Be(0);

        Directory.Delete(captureDirectory, recursive: true);
    }

    /// <summary>Verifies usage parser uses one terminal stream usage and keeps cached and reasoning as subsets.</summary>
    [Fact]
    public void UsageParser_UsesOneTerminalStreamUsage_AndKeepsCachedAndReasoningAsSubsets()
    {
        const string eventStream = "data: {\"id\":\"gen-1\",\"choices\":[],\"usage\":{\"prompt_tokens\":100,\"completion_tokens\":20,\"prompt_tokens_details\":{\"cached_tokens\":30},\"completion_tokens_details\":{\"reasoning_tokens\":8},\"cost\":0.0002}}\n\n"
            + "data: {\"id\":\"gen-1\",\"choices\":[],\"usage\":{\"prompt_tokens\":100,\"completion_tokens\":20,\"prompt_tokens_details\":{\"cached_tokens\":30},\"completion_tokens_details\":{\"reasoning_tokens\":8},\"cost\":0.0002}}\n\n"
            + "data: [DONE]\n\n";
        var parsed = RecordedUsageParser.Parse(Encoding.UTF8.GetBytes(eventStream), "text/event-stream");

        parsed.GenerationId.Should().Be("gen-1");
        parsed.Usage.Should().Be(new RecordedUsage(100, 20, 30, 8, null, 0.0002m, true));
        parsed.RawJson.Should().Contain("cached_tokens");
    }

    /// <summary>Verifies usage parser keeps missing or malformed usage unknown.</summary>
    [Fact]
    public void UsageParser_KeepsMissingOrMalformedUsageUnknown()
    {
        var missing = RecordedUsageParser.Parse(Encoding.UTF8.GetBytes("{}"), "application/json");
        var malformed = RecordedUsageParser.Parse(Encoding.UTF8.GetBytes("not-json"), "application/json");

        missing.Usage.Should().BeNull();
        malformed.Usage.Should().BeNull();
        malformed.RawJson.Should().BeNull();
    }

    /// <summary>Verifies usage parser accepts cr lf multiline terminal usage and marks truncated or malformed usage unknown.</summary>
    [Fact]
    public void UsageParser_AcceptsCrLfMultilineTerminalUsage_AndMarksTruncatedOrMalformedUsageUnknown()
    {
        const string eventStream = "data: {\"id\":\"crlf-id\",\r\n"
            + "data: \"choices\":[],\"usage\":{\"prompt_tokens\":12,\"completion_tokens\":4}}\r\n\r\n"
            + "data: [DONE]\r\n\r\n";
        const string truncated = "data: {\"id\":\"truncated-id\",\"choices\":[],\"usage\":{\"prompt_tokens\":12,\"completion_tokens\":4}}\r\n\r\n";
        const string malformed = "data: {\"id\":\"malformed-id\",\"usage\":{oops}}\r\n\r\n";

        var parsed = RecordedUsageParser.Parse(Encoding.UTF8.GetBytes(eventStream), "text/event-stream");
        var incomplete = RecordedUsageParser.Parse(Encoding.UTF8.GetBytes(truncated), "text/event-stream");
        var invalid = RecordedUsageParser.Parse(Encoding.UTF8.GetBytes(malformed), "text/event-stream");

        parsed.GenerationId.Should().Be("crlf-id");
        parsed.Usage.Should().Be(new RecordedUsage(12, 4, null, null, null, null, true));
        parsed.RawJson.Should().Contain("prompt_tokens");
        incomplete.Usage.Should().Be(new RecordedUsage(12, 4, null, null, null, null, false));
        invalid.Usage.Should().BeNull();
        invalid.RawJson.Should().BeNull();
    }

    /// <summary>Verifies fake mode rejects any upstream route.</summary>
    [Fact]
    public void FakeMode_RejectsAnyUpstreamRoute()
    {
        var provider = new FakeOpenRouterProvider([]);
        var action = () => new RecordingProxy(new RecordingProxyOptions(
            RecordingProxyMode.Fake, new Uri("https://openrouter.ai/api/v1/"), null, provider, "capture",
            new ComparisonSettings("qwen/qwen3.7-flash", "alibaba", 32768, true)));

        action.Should().Throw<ArgumentException>();
    }

    /// <summary>Verifies known overhead is serialized and unknown requests consume main allowance.</summary>
    [Fact]
    public async Task KnownOverhead_IsSerializedAndUnknownRequestsConsumeMainAllowance()
    {
        var captureDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var provider = new FakeOpenRouterProvider([Completion("compact"), Completion("helper"), Completion("main")]);
        await using var proxy = new RecordingProxy(new RecordingProxyOptions(
            RecordingProxyMode.Fake, null, null, provider, captureDirectory,
            new ComparisonSettings("qwen/qwen3.7-flash", "alibaba", 32768, true)));
        await proxy.StartAsync();
        var scope = proxy.BeginScope(new RecordingScopeOptions("classification-session", 1, 0, "child-key"));
        using var client = new HttpClient();

        var compactionCredential = scope.CredentialFor(RecordedRequestKind.Compaction);
        var helperCredential = scope.CredentialFor(RecordedRequestKind.Helper);
        (await SendAsync(client, scope, "compact", compactionCredential)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(client, scope, "helper", helperCredential)).StatusCode.Should().Be(HttpStatusCode.OK);
        var main = await SendAsync(client, scope, "main");
        var unknown = await SendAsync(client, scope, "unknown");
        await WaitForAttemptsAsync(proxy, 4);

        main.StatusCode.Should().Be(HttpStatusCode.OK);
        unknown.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        scope.MainCallsConsumed.Should().Be(1);
        proxy.Attempts.Select(attempt => attempt.Kind).Should().ContainInOrder(
            RecordedRequestKind.Compaction, RecordedRequestKind.Helper, RecordedRequestKind.Unknown, RecordedRequestKind.Unknown);
        proxy.Attempts.Select(attempt => attempt.Sequence).Should().OnlyHaveUniqueItems();
        Directory.Delete(captureDirectory, recursive: true);
    }

    /// <summary>Verifies helper credential remains overhead when interleaved with main credential at budget boundary.</summary>
    [Fact]
    public async Task HelperCredentialRemainsOverhead_WhenInterleavedWithMainCredentialAtBudgetBoundary()
    {
        var captureDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var provider = new FakeOpenRouterProvider([Completion("helper"), Completion("main")]);
        await using var proxy = new RecordingProxy(new RecordingProxyOptions(
            RecordingProxyMode.Fake, null, null, provider, captureDirectory,
            new ComparisonSettings("qwen/qwen3.7-flash", "alibaba", 32768, true)));
        await proxy.StartAsync();
        var scope = proxy.BeginScope(new RecordingScopeOptions("interleaved-session", 2, 1, "main-child-key"));
        using var client = new HttpClient();

        var helperCredential = scope.CredentialFor(RecordedRequestKind.Helper);
        var helper = await SendAsync(client, scope, "helper", helperCredential);
        var main = await SendAsync(client, scope, "main");
        var mainAtBoundary = await SendAsync(client, scope, "main at boundary");
        await proxy.DrainAsync();

        helper.StatusCode.Should().Be(HttpStatusCode.OK);
        main.StatusCode.Should().Be(HttpStatusCode.OK);
        mainAtBoundary.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        scope.MainCallsConsumed.Should().Be(1);
        scope.AgentCallsConsumed.Should().Be(1);
        proxy.Attempts.Select(attempt => attempt.Kind).Should().ContainInOrder(
            RecordedRequestKind.Helper, RecordedRequestKind.Unknown, RecordedRequestKind.Unknown);
        proxy.Attempts.Select(attempt => attempt.BudgetRejected).Should().Equal(false, false, true);
        provider.Requests.Should().HaveCount(2);
        Directory.Delete(captureDirectory, recursive: true);
    }

    /// <summary>Verifies session credentials keep attempts associated with their own budgets.</summary>
    [Fact]
    public async Task SessionCredentials_KeepAttemptsAssociatedWithTheirOwnBudgets()
    {
        var captureDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var provider = new FakeOpenRouterProvider([Completion("first"), Completion("second")]);
        await using var proxy = new RecordingProxy(new RecordingProxyOptions(
            RecordingProxyMode.Fake, null, null, provider, captureDirectory,
            new ComparisonSettings("qwen/qwen3.7-flash", "alibaba", 32768, true)));
        await proxy.StartAsync();
        var firstScope = proxy.BeginScope(new RecordingScopeOptions("first-session", 1, 0, "first-child-key"));
        var secondScope = proxy.BeginScope(new RecordingScopeOptions("second-session", 1, 0, "second-child-key"));
        using var client = new HttpClient();

        (await SendAsync(client, firstScope, "first payload")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(client, secondScope, "second payload")).StatusCode.Should().Be(HttpStatusCode.OK);
        await proxy.DrainAsync();

        proxy.Attempts.Should().HaveCount(2);
        proxy.Attempts.Select(attempt => attempt.SessionId).Should().Equal("first-session", "second-session");
        var requestBodies = proxy.Attempts.Select(attempt => Encoding.UTF8.GetString(attempt.RequestBody)).ToArray();
        requestBodies.Should().ContainSingle(body => body.Contains("first payload", StringComparison.Ordinal));
        requestBodies.Should().ContainSingle(body => body.Contains("second payload", StringComparison.Ordinal));
        firstScope.MainCallsConsumed.Should().Be(1);
        secondScope.MainCallsConsumed.Should().Be(1);
        foreach (var capturedFile in Directory.EnumerateFiles(captureDirectory, "*", SearchOption.AllDirectories))
        {
            var content = await File.ReadAllTextAsync(capturedFile);
            content.Should().NotContain("first-child-key").And.NotContain("second-child-key");
        }
        Directory.Delete(captureDirectory, recursive: true);
    }

    /// <summary>Verifies malformed provider request is retained as afailed attempt.</summary>
    [Fact]
    public async Task MalformedProviderRequest_IsRetainedAsAFailedAttempt()
    {
        var captureDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        await using var proxy = new RecordingProxy(new RecordingProxyOptions(
            RecordingProxyMode.Fake, null, null, new FakeOpenRouterProvider([]), captureDirectory,
            new ComparisonSettings("qwen/qwen3.7-flash", "alibaba", 32768, true)));
        await proxy.StartAsync();
        var scope = proxy.BeginScope(new RecordingScopeOptions("malformed-session", 2, 0, "child-key"));
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(scope.Endpoint, "chat/completions"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", scope.Credential);
        request.Content = new StringContent("{broken", Encoding.UTF8, "application/json");

        var response = await client.SendAsync(request);
        await proxy.DrainAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        proxy.Attempts.Should().ContainSingle(attempt => attempt.Error == "invalid provider request"
            && attempt.StatusCode == (int)HttpStatusCode.BadRequest && attempt.ResponseBody != null);
        Directory.Delete(captureDirectory, recursive: true);
    }

    /// <summary>Verifies invalid provider shapes are rejected without forwarding and listener continues.</summary>
    [Fact]
    public async Task InvalidProviderShapes_AreRejectedWithoutForwardingAndListenerContinues()
    {
        var captureDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var provider = new FakeOpenRouterProvider([Completion("valid-after-invalid")]);
        await using var proxy = new RecordingProxy(new RecordingProxyOptions(
            RecordingProxyMode.Fake, null, null, provider, captureDirectory,
            new ComparisonSettings("qwen/qwen3.7-flash", "alibaba", 32768, true)));
        await proxy.StartAsync();
        var scope = proxy.BeginScope(new RecordingScopeOptions("invalid-shapes-session", 6, 0, "shape-child-key"));
        using var client = new HttpClient();

        foreach (var body in new[] { "null", "[]", "{\"messages\":[]}", "{\"messages\":null}", "{\"messages\":{}}" })
        {
            using var response = await SendRawAsync(client, scope, body);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest, because: body);
        }

        using var validResponse = await SendAsync(client, scope, "valid after invalid requests");
        await proxy.DrainAsync();

        validResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        provider.Requests.Should().ContainSingle();
        proxy.Attempts.Should().HaveCount(6);
        proxy.Attempts.Take(5).Should().OnlyContain(attempt => attempt.BudgetRejected
            && attempt.StatusCode == (int)HttpStatusCode.BadRequest
            && attempt.Error == "invalid provider request");
        proxy.Attempts.Last().StatusCode.Should().Be((int)HttpStatusCode.OK);
        Directory.Delete(captureDirectory, recursive: true);
    }

    /// <summary>Verifies fake provider serves sse tool calls and final usage and records exact bytes.</summary>
    [Fact]
    public async Task FakeProvider_ServesSseToolCallsAndFinalUsage_AndRecordsExactBytes()
    {
        var captureDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var usageDocument = JsonDocument.Parse(
            "{\"prompt_tokens\":14,\"completion_tokens\":5,\"completion_tokens_details\":{\"reasoning_tokens\":2}}");
        var fake = new FakeOpenRouterProvider([
            new FakeCompletion(null, [new FakeToolCall("call-1", "read_file", "{\"path\":\"README.md\"}")],
                usageDocument.RootElement.Clone(), "fake-tool")
        ]);
        await using var proxy = new RecordingProxy(new RecordingProxyOptions(
            RecordingProxyMode.Fake, null, null, fake, captureDirectory,
            new ComparisonSettings("qwen/qwen3.7-flash", "alibaba", 32768, true)));
        await proxy.StartAsync();
        var scope = proxy.BeginScope(new RecordingScopeOptions("stream-session", 2, 0, "stream-key"));
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(scope.Endpoint, "chat/completions"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", scope.Credential);
        request.Content = new StringContent(
            "{\"model\":\"wrong\",\"models\":[\"fallback/model\"],\"max_completion_tokens\":9,\"reasoning_effort\":\"low\",\"include_reasoning\":false,\"stream\":true,\"stream_options\":{\"include_usage\":true},\"messages\":[{\"role\":\"user\",\"content\":\"synthetic\"}]}",
            Encoding.UTF8,
            "application/json");

        using var response = await client.SendAsync(request);
        var receivedBytes = await response.Content.ReadAsByteArrayAsync();
        await WaitForAttemptsAsync(proxy, 1);
        var attempt = proxy.Attempts.Should().ContainSingle().Which;

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        receivedBytes.Should().Equal(attempt.ResponseBody!);
        Encoding.UTF8.GetString(receivedBytes).Should().Contain("call-1").And.Contain("read_file").And.Contain("[DONE]");
        attempt.GenerationId.Should().Be("fake-tool");
        attempt.Usage.Should().Be(new RecordedUsage(14, 5, null, 2, null, null, true));
        attempt.RequestedModel.Should().Be("wrong");
        attempt.ReturnedModel.Should().Be("qwen/qwen3.7-flash");
        attempt.CaptureFiles!.Normalization.Should().NotBeNull();
        var normalizationPath = Path.Combine(captureDirectory, attempt.CaptureFiles.Normalization!);
        using var normalization = JsonDocument.Parse(await File.ReadAllTextAsync(normalizationPath));
        normalization.RootElement.EnumerateArray().Select(field => field.GetProperty("Field").GetString())
            .Should().Contain(new[] { "model", "models", "max_completion_tokens", "reasoning_effort", "include_reasoning", "max_tokens", "reasoning", "provider", "transforms" });
        var effectiveBody = Encoding.UTF8.GetString(fake.Requests.Single());
        effectiveBody.Should().Contain("qwen/qwen3.7-flash").And.NotContain("reasoning_effort").And.NotContain("max_completion_tokens");
        Directory.Delete(captureDirectory, recursive: true);
    }

    /// <summary>Verifies upstream sse is forwarded before the provider finishes and partial bytes are captured.</summary>
    [Fact]
    public async Task UpstreamSse_IsForwardedBeforeTheProviderFinishes_AndPartialBytesAreCaptured()
    {
        var captureDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var handler = new BlockingSseHandler();
        var upstreamCredential = "upstream-test-secret";
        await using var proxy = new RecordingProxy(new RecordingProxyOptions(
            RecordingProxyMode.Live,
            new Uri("https://openrouter.ai/api/v1/"),
            upstreamCredential,
            null,
            captureDirectory,
            new ComparisonSettings("qwen/qwen3.7-flash", "alibaba", 32768, true),
            handler) { AuthorizeForwardAsync = _ => Task.FromResult(true) });
        await proxy.StartAsync();
        var scope = proxy.BeginScope(new RecordingScopeOptions("live-stream-session", 2, 0, "child-stream-key"));
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(scope.Endpoint, "chat/completions"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", scope.Credential);
        request.Content = new StringContent("{\"model\":\"wrong\",\"stream\":true,\"messages\":[{\"role\":\"user\",\"content\":\"synthetic\"}]}", Encoding.UTF8,
            "application/json");

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        await using var responseStream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(responseStream, Encoding.UTF8, leaveOpen: true);
        var firstEvent = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2));
        firstEvent.Should().StartWith("data:");
        await handler.WaitForReleaseAsync().WaitAsync(TimeSpan.FromSeconds(2));
        handler.IsWaitingForRelease.Should().BeTrue();
        handler.UpstreamAuthorization.Should().Be($"Bearer {upstreamCredential}");
        handler.UpstreamBody.Should().Contain("qwen/qwen3.7-flash");
        handler.Release();
        var remaining = await reader.ReadToEndAsync();
        await WaitForAttemptsAsync(proxy, 1);
        var attempt = proxy.Attempts.Should().ContainSingle().Which;

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (firstEvent + "\n" + remaining).Should().Contain("[DONE]");
        Encoding.UTF8.GetString(attempt.ResponseBody!).Should().Contain("[DONE]");
        attempt.Usage?.Complete.Should().BeTrue();
        attempt.CaptureFiles?.Response.Should().EndWith("response.sse");
        Directory.Delete(captureDirectory, recursive: true);
    }

    /// <summary>Verifies cancellation during stream persists the received partial response.</summary>
    [Fact]
    public async Task CancellationDuringStream_PersistsTheReceivedPartialResponse()
    {
        var captureDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var handler = new BlockingSseHandler();
        var proxy = new RecordingProxy(new RecordingProxyOptions(
            RecordingProxyMode.Live,
            new Uri("https://openrouter.ai/api/v1/"),
            "upstream-test-secret",
            null,
            captureDirectory,
            new ComparisonSettings("qwen/qwen3.7-flash", "alibaba", 32768, true),
            handler) { AuthorizeForwardAsync = _ => Task.FromResult(true) });
        await proxy.StartAsync();
        var scope = proxy.BeginScope(new RecordingScopeOptions("cancelled-stream-session", 2, 0, "child-stream-key"));
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(scope.Endpoint, "chat/completions"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", scope.Credential);
        request.Content = new StringContent("{\"model\":\"wrong\",\"stream\":true,\"messages\":[{\"role\":\"user\",\"content\":\"synthetic\"}]}", Encoding.UTF8,
            "application/json");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        await using var responseStream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(responseStream, Encoding.UTF8, leaveOpen: true);

        (await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2))).Should().StartWith("data:");
        await handler.WaitForReleaseAsync().WaitAsync(TimeSpan.FromSeconds(2));
        await proxy.DisposeAsync();

        var attempt = proxy.Attempts.Should().ContainSingle().Which;
        attempt.Error.Should().Be("TaskCanceledException");
        Encoding.UTF8.GetString(attempt.ResponseBody!).Should().Contain("sse-id").And.NotContain("[DONE]");
        attempt.CaptureFiles?.Response.Should().EndWith("response.sse");
        Directory.Delete(captureDirectory, recursive: true);
    }

    /// <summary>Verifies provider retries are recorded as distinct attempts.</summary>
    [Fact]
    public async Task ProviderRetries_AreRecordedAsDistinctAttempts()
    {
        var captureDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var provider = new FakeOpenRouterProvider([Completion("retry-1"), Completion("retry-2")]);
        await using var proxy = new RecordingProxy(new RecordingProxyOptions(
            RecordingProxyMode.Fake, null, null, provider, captureDirectory,
            new ComparisonSettings("qwen/qwen3.7-flash", "alibaba", 32768, true)));
        await proxy.StartAsync();
        var scope = proxy.BeginScope(new RecordingScopeOptions("retry-session", 3, 0, "retry-key"));
        using var client = new HttpClient();

        (await SendAsync(client, scope, "same request")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(client, scope, "same request")).StatusCode.Should().Be(HttpStatusCode.OK);
        await WaitForAttemptsAsync(proxy, 2);

        proxy.Attempts.Select(attempt => attempt.Sequence).Should().Equal(1, 2);
        proxy.Attempts.Select(attempt => attempt.GenerationId).Should().Equal("fake-retry-1", "fake-retry-2");
        provider.Requests.Should().HaveCount(2);
        Directory.Delete(captureDirectory, recursive: true);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, RecordingScope scope, string content, string? credential = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(scope.Endpoint, "chat/completions"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential ?? scope.Credential);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { model = "untrusted/model", messages = new[] { new { role = "user", content } } }),
            Encoding.UTF8,
            "application/json");
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendRawAsync(HttpClient client, RecordingScope scope, string body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(scope.Endpoint, "chat/completions"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", scope.Credential);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return await client.SendAsync(request);
    }

    private static FakeCompletion Completion(string id)
    {
        using var usageDocument = JsonDocument.Parse("{\"prompt_tokens\":10,\"completion_tokens\":2}");
        return new FakeCompletion(id, [], usageDocument.RootElement.Clone(), $"fake-{id}");
    }

    private static FakeCompletion ToolCompletion(string id, string toolName)
    {
        using var usageDocument = JsonDocument.Parse("{\"prompt_tokens\":10,\"completion_tokens\":2}");
        return new FakeCompletion(null,
            [new FakeToolCall($"call-{id}", toolName, "{\"path\":\"README.md\"}")],
            usageDocument.RootElement.Clone(),
            $"fake-{id}");
    }

    private static async Task WaitForAttemptsAsync(RecordingProxy proxy, int count)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (proxy.Attempts.Count < count && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(10);
        proxy.Attempts.Should().HaveCount(count);
    }

    private sealed class BlockingSseHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _waitingForRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly byte[] _first = Encoding.UTF8.GetBytes("data: {\"id\":\"sse-id\",\"choices\":[],\"usage\":null}\n\n");
        private readonly byte[] _second = Encoding.UTF8.GetBytes(
            "data: {\"id\":\"sse-id\",\"choices\":[],\"usage\":{\"prompt_tokens\":4,\"completion_tokens\":2}}\n\n"
            + "data: [DONE]\n\n");

        public bool IsWaitingForRelease { get; private set; }

        public string? UpstreamAuthorization { get; private set; }

        public string? UpstreamBody { get; private set; }

        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.UpstreamAuthorization = request.Headers.Authorization?.ToString();
            this.UpstreamBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new GatedSseStream(this)),
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
            return response;
        }

        internal void Release() => this._release.TrySetResult();

        internal Task WaitForReleaseAsync() => this._waitingForRelease.Task;

        internal async ValueTask<int> ReadAsync(Memory<byte> destination, int index, CancellationToken cancellationToken)
        {
            byte[] bytes;
            if (index == 0)
                bytes = this._first;
            else if (index == 1)
            {
                this.IsWaitingForRelease = true;
                this._waitingForRelease.TrySetResult();
                await this._release.Task.WaitAsync(cancellationToken);
                bytes = this._second;
            }
            else
                return 0;
            bytes.AsMemory().CopyTo(destination);
            return bytes.Length;
        }

        private sealed class GatedSseStream(BlockingSseHandler handler) : Stream
        {
            private int _index;

            /// <inheritdoc />
            public override bool CanRead => true;

            /// <inheritdoc />
            public override bool CanSeek => false;

            /// <inheritdoc />
            public override bool CanWrite => false;

            /// <inheritdoc />
            public override long Length => throw new NotSupportedException();

            /// <inheritdoc />
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            /// <inheritdoc />
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            /// <inheritdoc />
            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
                handler.ReadAsync(buffer, this._index++, cancellationToken);

            /// <inheritdoc />
            public override void Flush() => throw new NotSupportedException();

            /// <inheritdoc />
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            /// <inheritdoc />
            public override void SetLength(long value) => throw new NotSupportedException();

            /// <inheritdoc />
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
