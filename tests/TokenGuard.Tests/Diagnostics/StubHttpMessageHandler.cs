using System.Net;
using System.Text;

namespace TokenGuard.Tests.Diagnostics;

/// <summary>
///     Represents an HTTP handler that answers every request with one fixed JSON response.
/// </summary>
/// <param name="statusCode">The status code of the response.</param>
/// <param name="json">The JSON body of the response.</param>
internal sealed class StubHttpMessageHandler(HttpStatusCode statusCode, string json) : HttpMessageHandler
{
    /// <summary>
    ///     Gets the body of the last request, or <see langword="null" /> before the first request or when it had no body.
    /// </summary>
    public string? LastRequestBody { get; private set; }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        this.LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
            RequestMessage = request,
        };
    }
}
