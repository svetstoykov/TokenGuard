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
    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
            RequestMessage = request,
        });
}
