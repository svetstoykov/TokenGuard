extern alias sample;

using System.ClientModel.Primitives;
using System.Net;
using FluentAssertions;
using sample::Codexplorer.Configuration;

namespace Codexplorer.Automation.Tests.Configuration;

/// <summary>Verifies how many rate-limited replies a model request survives.</summary>
public sealed class FixedDelayRetryPolicyTests
{
    /// <summary>Verifies a request succeeds when the provider stops rate limiting within the retry allowance.</summary>
    [Fact]
    public async Task Send_RateLimitedForEveryAllowedRetry_ReturnsTheLaterSuccess()
    {
        using var handler = new RateLimitingHandler(rateLimitedReplies: 3);

        var status = await SendAsync(handler, maxRetries: 3);

        status.Should().Be(200);
    }

    /// <summary>Verifies a request ends with the rate-limit reply when the provider outlasts the retry allowance.</summary>
    [Fact]
    public async Task Send_RateLimitedBeyondTheAllowedRetries_ReturnsTheRateLimitReply()
    {
        using var handler = new RateLimitingHandler(rateLimitedReplies: 4);

        var status = await SendAsync(handler, maxRetries: 3);

        status.Should().Be(429);
    }

    private static async Task<int> SendAsync(RateLimitingHandler handler, int maxRetries)
    {
        using var httpClient = new HttpClient(handler);
        var pipeline = ClientPipeline.Create(new ClientPipelineOptions
        {
            Transport = new HttpClientPipelineTransport(httpClient),
            RetryPolicy = new FixedDelayRetryPolicy(maxRetries, TimeSpan.Zero)
        });
        using var message = pipeline.CreateMessage();
        message.Request.Method = "POST";
        message.Request.Uri = new Uri("https://provider.invalid/chat/completions");

        await pipeline.SendAsync(message);

        return message.Response!.Status;
    }

    private sealed class RateLimitingHandler(int rateLimitedReplies) : HttpMessageHandler
    {
        private int _remaining = rateLimitedReplies;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var status = this._remaining-- > 0 ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK;

            return Task.FromResult(new HttpResponseMessage(status));
        }
    }
}
