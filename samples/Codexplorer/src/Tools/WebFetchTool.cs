using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using SmartReader;
using TokenGuard.Core.Abstractions;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using WorkspaceModel = Codexplorer.Workspace.Workspace;

namespace Codexplorer.Tools;

/// <summary>
/// Fetches one public web page and returns readable plain text for the model.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="WebFetchTool"/> is Codexplorer's bridge from URL-level web navigation into
/// TokenGuard-managed context. It fetches content with browser-like headers, follows a bounded number
/// of redirects, and extracts readable text so the agent can inspect public documentation and articles
/// without receiving raw HTML boilerplate.
/// </para>
/// <para>
/// HTML responses are decoded from bytes using explicit charset handling before they are passed through
/// Mozilla Readability semantics via SmartReader. Non-HTML text payloads stay textual, while binary
/// payloads return explicit status messages instead of unreadable output.
/// </para>
/// <para>
/// Result size is capped with the injected <see cref="ITokenCounter"/> so tool output uses the same
/// token-accounting abstraction that TokenGuard uses for conversation budgeting.
/// </para>
/// </remarks>
public sealed class WebFetchTool : IWorkspaceTool
{
    /// <summary>
    /// Gets the name of the named <see cref="HttpClient"/> registration used by this tool.
    /// </summary>
    public const string HttpClientName = "web-fetch";

    /// <summary>
    /// Gets the browser user agent sent with fetch requests.
    /// </summary>
    public const string BrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/135.0.0.0 Safari/537.36";

    /// <summary>
    /// Gets the per-request timeout, in seconds, applied to the named <see cref="HttpClient"/>.
    /// </summary>
    public const int TimeoutSeconds = 20;

    /// <summary>
    /// Gets the default token ceiling applied when <c>max_tokens</c> is omitted.
    /// </summary>
    public const int DefaultMaxTokens = 4000;

    /// <summary>
    /// Gets the highest accepted value for <c>max_tokens</c>.
    /// </summary>
    public const int MaximumMaxTokens = 12000;

    private const int RedirectLimit = 5;
    private const int HtmlEncodingProbeBytes = 8192;

    private static readonly ToolSchema CachedSchema = ToolSchema.CreateFunction(
        "web_fetch",
        "Fetch one public HTTP or HTTPS URL and return readable plain text. Best for docs, articles, README pages, Stack Overflow answers, and other non-JavaScript-gated pages. Use max_tokens to cap result size.",
        """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "url": {
              "type": "string",
              "description": "Fully-qualified HTTP or HTTPS URL to fetch."
            },
            "max_tokens": {
              "type": "integer",
              "description": "Approximate token ceiling for returned content. Defaults to 4000 and caps at 12000.",
              "default": 4000,
              "maximum": 12000
            }
          },
          "required": ["url"]
        }
        """);

    private static readonly Regex MetaCharsetPattern = new(
        """<meta\s+[^>]*charset\s*=\s*["']?\s*(?<charset>[A-Za-z0-9_\-]+)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MetaContentTypeCharsetPattern = new(
        """<meta\s+[^>]*content\s*=\s*["'][^"']*charset\s*=\s*(?<charset>[A-Za-z0-9_\-]+)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex WhitespacePattern = new(@"\s+", RegexOptions.Compiled);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ITokenCounter _tokenCounter;

    static WebFetchTool()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebFetchTool"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Factory used to resolve the named <see cref="HttpClient"/>.</param>
    /// <param name="tokenCounter">Token counter used to cap returned content.</param>
    public WebFetchTool(IHttpClientFactory httpClientFactory, ITokenCounter tokenCounter)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(tokenCounter);

        this._httpClientFactory = httpClientFactory;
        this._tokenCounter = tokenCounter;
    }

    /// <summary>
    /// Gets the tool name exposed to the model.
    /// </summary>
    public string Name => "web_fetch";

    /// <summary>
    /// Gets the cached OpenAI-compatible schema for this tool.
    /// </summary>
    public ToolSchema Schema => CachedSchema;

    /// <summary>
    /// Represents arguments for <see cref="WebFetchTool"/>.
    /// </summary>
    /// <param name="Url">The fully-qualified HTTP or HTTPS URL to fetch.</param>
    /// <param name="MaxTokens">The optional approximate token ceiling for returned content.</param>
    public sealed record Parameters(string Url, int? MaxTokens);

    Task<string> IWorkspaceTool.ExecuteAsync(JsonElement arguments, WorkspaceModel workspace, CancellationToken ct)
    {
        return this.HandleAsync(ToolRegistry.DeserializeArguments<Parameters>(arguments), ct);
    }

    /// <summary>
    /// Fetches one URL and returns readable plain text or a descriptive error string.
    /// </summary>
    /// <param name="parameters">Typed tool arguments.</param>
    /// <param name="ct">The cancellation token for the current tool call.</param>
    /// <returns>The extracted text or a descriptive failure message.</returns>
    public async Task<string> HandleAsync(Parameters parameters, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        if (!TryCreateSupportedUri(parameters.Url, out var requestedUri))
        {
            return $"[Error] URL must be an absolute HTTP or HTTPS URL: {parameters.Url}";
        }

        var maxTokens = parameters.MaxTokens ?? DefaultMaxTokens;
        if (maxTokens <= 0)
        {
            return $"[Error] max_tokens must be greater than 0: {maxTokens}";
        }

        maxTokens = Math.Min(maxTokens, MaximumMaxTokens);

        var fetchOutcome = await this.FetchAsync(requestedUri, ct).ConfigureAwait(false);
        if (fetchOutcome.Error is not null)
        {
            return fetchOutcome.Error;
        }

        var resource = fetchOutcome.Resource
            ?? throw new InvalidOperationException("Web fetch completed without a resource or error.");

        var extractedText = this.ExtractResponseText(requestedUri, resource);
        return this.TruncateToTokenLimit(extractedText, maxTokens);
    }

    private string ExtractResponseText(Uri requestedUri, FetchedResource resource)
    {
        if (IsPdf(resource.MediaType))
        {
            return $"[PDF] This page is a PDF and cannot be read as text. URL: {requestedUri.AbsoluteUri}";
        }

        if (ShouldTreatAsHtml(resource.MediaType, resource.Body))
        {
            var html = DecodeHtml(resource.Body, resource.Charset);
            var readableText = ExtractReadableHtmlText(resource.EffectiveUri, html);
            return string.IsNullOrWhiteSpace(readableText)
                ? $"[No readable content extracted from: {requestedUri.AbsoluteUri}. The page may require JavaScript rendering.]"
                : readableText;
        }

        if (IsJson(resource.MediaType) || IsText(resource.MediaType))
        {
            return DecodeText(resource.Body, resource.Charset).Trim();
        }

        var contentType = string.IsNullOrWhiteSpace(resource.MediaType) ? "unknown" : resource.MediaType;
        return $"[Binary] Content-Type {contentType} cannot be read as text.";
    }

    private async Task<FetchOutcome> FetchAsync(Uri requestedUri, CancellationToken ct)
    {
        var client = this._httpClientFactory.CreateClient(HttpClientName);
        var visitedUris = new HashSet<string>(StringComparer.Ordinal);
        var currentUri = requestedUri;

        for (var redirectCount = 0; redirectCount <= RedirectLimit; redirectCount++)
        {
            if (!visitedUris.Add(currentUri.AbsoluteUri))
            {
                return FetchOutcome.FromError($"[Error] Redirect loop detected for: {requestedUri.AbsoluteUri}");
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, currentUri);

            try
            {
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);

                if (IsRedirect(response.StatusCode))
                {
                    if (response.Headers.Location is null)
                    {
                        return FetchOutcome.FromError(BuildHttpStatusError(response, requestedUri));
                    }

                    if (redirectCount == RedirectLimit)
                    {
                        return FetchOutcome.FromError($"[Error] Too many redirects for: {requestedUri.AbsoluteUri}");
                    }

                    currentUri = response.Headers.Location.IsAbsoluteUri
                        ? response.Headers.Location
                        : new Uri(currentUri, response.Headers.Location);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    return FetchOutcome.FromError(BuildHttpStatusError(response, requestedUri));
                }

                var body = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                return FetchOutcome.FromResource(new FetchedResource(
                    currentUri,
                    response.Content.Headers.ContentType?.MediaType,
                    response.Content.Headers.ContentType?.CharSet,
                    body));
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return FetchOutcome.FromError($"[Timeout] Page did not respond within 20 seconds: {requestedUri.AbsoluteUri}");
            }
            catch (HttpRequestException ex) when (IsDnsFailure(ex))
            {
                return FetchOutcome.FromError($"[Error] Could not resolve host: {requestedUri.Host}");
            }
            catch (HttpRequestException ex) when (IsConnectionRefused(ex))
            {
                return FetchOutcome.FromError($"[Error] Connection refused: {requestedUri.AbsoluteUri}");
            }
            catch (HttpRequestException ex)
            {
                return FetchOutcome.FromError($"[Error] {ex.Message}: {requestedUri.AbsoluteUri}");
            }
        }

        return FetchOutcome.FromError($"[Error] Too many redirects for: {requestedUri.AbsoluteUri}");
    }

    private string ExtractReadableHtmlText(Uri effectiveUri, string html)
    {
        var article = Reader.ParseArticle(effectiveUri.AbsoluteUri, html, BrowserUserAgent);
        if (article.IsReadable && !string.IsNullOrWhiteSpace(article.TextContent))
        {
            return NormalizeReadableText(article.TextContent);
        }

        var fallbackText = StripHtmlToText(html);
        return string.IsNullOrWhiteSpace(fallbackText) ? string.Empty : fallbackText;
    }

    private string DecodeHtml(byte[] body, string? charsetFromHeader)
    {
        if (TryGetEncoding(charsetFromHeader, out var headerEncoding))
        {
            return headerEncoding.GetString(body);
        }

        if (TryDetectEncodingFromMeta(body, out var metaEncoding))
        {
            return metaEncoding.GetString(body);
        }

        return Encoding.UTF8.GetString(body);
    }

    private static string DecodeText(byte[] body, string? charsetFromHeader)
    {
        if (TryGetEncoding(charsetFromHeader, out var headerEncoding))
        {
            return headerEncoding.GetString(body);
        }

        return Encoding.UTF8.GetString(body);
    }

    private string TruncateToTokenLimit(string content, int maxTokens)
    {
        var normalizedContent = content.Trim();
        if (string.IsNullOrEmpty(normalizedContent))
        {
            return normalizedContent;
        }

        if (this.CountTokens(normalizedContent) <= maxTokens)
        {
            return normalizedContent;
        }

        var truncationNotice = $"\n[Content truncated at {maxTokens} tokens. Use a smaller range or request a specific section.]";
        var low = 0;
        var high = normalizedContent.Length;

        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            var candidate = normalizedContent[..mid].TrimEnd() + truncationNotice;

            if (this.CountTokens(candidate) <= maxTokens)
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }

        var prefix = normalizedContent[..low].TrimEnd();
        while (prefix.Length > 0 && this.CountTokens(prefix + truncationNotice) > maxTokens)
        {
            prefix = prefix[..^1].TrimEnd();
        }

        return string.IsNullOrEmpty(prefix)
            ? truncationNotice.TrimStart('\n')
            : prefix + truncationNotice;
    }

    private int CountTokens(string content)
    {
        return this._tokenCounter.Count(ContextMessage.FromText(MessageRole.Tool, content));
    }

    private static bool ShouldTreatAsHtml(string? mediaType, byte[] body)
    {
        if (IsHtmlMediaType(mediaType))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(mediaType))
        {
            return false;
        }

        var sampleLength = Math.Min(body.Length, HtmlEncodingProbeBytes);
        var sample = Encoding.Latin1.GetString(body, 0, sampleLength).TrimStart();
        return sample.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase)
            || sample.StartsWith("<html", StringComparison.OrdinalIgnoreCase)
            || sample.StartsWith("<head", StringComparison.OrdinalIgnoreCase)
            || sample.StartsWith("<body", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHtmlMediaType(string? mediaType)
    {
        return mediaType is not null
               && (mediaType.Equals("text/html", StringComparison.OrdinalIgnoreCase)
                   || mediaType.Equals("application/xhtml+xml", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsJson(string? mediaType)
    {
        return mediaType is not null
               && (mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
                   || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsText(string? mediaType)
    {
        return mediaType is not null && mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPdf(string? mediaType)
    {
        return mediaType is not null && mediaType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetEncoding(string? charset, out Encoding encoding)
    {
        if (!string.IsNullOrWhiteSpace(charset))
        {
            try
            {
                encoding = Encoding.GetEncoding(charset.Trim().Trim('"', '\''));
                return true;
            }
            catch (ArgumentException)
            {
            }
        }

        encoding = Encoding.UTF8;
        return false;
    }

    private static bool TryDetectEncodingFromMeta(byte[] body, out Encoding encoding)
    {
        var sampleLength = Math.Min(body.Length, HtmlEncodingProbeBytes);
        var sample = Encoding.Latin1.GetString(body, 0, sampleLength);

        var charset = MetaCharsetPattern.Match(sample).Groups["charset"].Value;
        if (string.IsNullOrWhiteSpace(charset))
        {
            charset = MetaContentTypeCharsetPattern.Match(sample).Groups["charset"].Value;
        }

        return TryGetEncoding(charset, out encoding);
    }

    private static string NormalizeReadableText(string content)
    {
        return content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
    }

    private static string StripHtmlToText(string html)
    {
        var parser = new HtmlParser();
        var document = parser.ParseDocument(html);
        var text = document.DocumentElement?.TextContent ?? string.Empty;
        return CollapseWhitespace(text);
    }

    private static string CollapseWhitespace(string content)
    {
        return WhitespacePattern.Replace(content, " ").Trim();
    }

    private static string BuildHttpStatusError(HttpResponseMessage response, Uri requestedUri)
    {
        var reason = string.IsNullOrWhiteSpace(response.ReasonPhrase) ? "Request failed" : response.ReasonPhrase;
        return $"[HTTP {(int)response.StatusCode}] {reason}: {requestedUri.AbsoluteUri}";
    }

    private static bool IsRedirect(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.Moved
            or HttpStatusCode.Redirect
            or HttpStatusCode.RedirectMethod
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;
    }

    private static bool IsDnsFailure(HttpRequestException exception)
    {
        return exception.InnerException is SocketException socketException
               && socketException.SocketErrorCode is SocketError.HostNotFound or SocketError.NoData;
    }

    private static bool IsConnectionRefused(HttpRequestException exception)
    {
        return exception.InnerException is SocketException socketException
               && socketException.SocketErrorCode == SocketError.ConnectionRefused;
    }

    private static bool TryCreateSupportedUri(string? candidate, out Uri uri)
    {
        if (Uri.TryCreate(candidate, UriKind.Absolute, out uri!))
        {
            return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
        }

        uri = null!;
        return false;
    }

    private sealed record FetchOutcome(FetchedResource? Resource, string? Error)
    {
        public static FetchOutcome FromError(string error) => new(null, error);

        public static FetchOutcome FromResource(FetchedResource resource) => new(resource, null);
    }

    private sealed record FetchedResource(Uri EffectiveUri, string? MediaType, string? Charset, byte[] Body);
}
