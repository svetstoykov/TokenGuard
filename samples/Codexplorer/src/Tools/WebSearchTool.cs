using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using WorkspaceModel = Codexplorer.Workspace.Workspace;

namespace Codexplorer.Tools;

/// <summary>
/// Searches the public web through the Brave Search API and returns compact plain-text results.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="WebSearchTool"/> is intended to give the agent lightweight discovery over public web
/// sources before it fetches full page content. The output stays intentionally compact so repeated
/// search iterations add moderate, predictable TokenGuard pressure instead of raw JSON overhead.
/// </para>
/// <para>
/// The tool does not throw for missing credentials, HTTP failures, or network errors. It always
/// returns a readable status string so the surrounding agent loop can continue.
/// </para>
/// </remarks>
public sealed class WebSearchTool : IWorkspaceTool
{
    /// <summary>
    /// Gets the name of the named <see cref="HttpClient"/> registration used by this tool.
    /// </summary>
    public const string HttpClientName = "brave-search";

    /// <summary>
    /// Gets the per-request timeout, in seconds, applied to the named <see cref="HttpClient"/>.
    /// </summary>
    public const int TimeoutSeconds = 10;

    /// <summary>
    /// Gets the default number of results returned when <c>count</c> is omitted.
    /// </summary>
    public const int DefaultCount = 5;

    /// <summary>
    /// Gets the maximum accepted value for <c>count</c>.
    /// </summary>
    public const int MaximumCount = 10;

    private static readonly Uri SearchEndpoint = new("https://api.search.brave.com/res/v1/web/search");

    private static readonly ToolSchema CachedSchema = ToolSchema.CreateFunction(
        "web_search",
        "Search public web results by query and return compact ranked title, URL, and snippet entries. Use this to discover candidate URLs before calling web_fetch.",
        """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "query": {
              "type": "string",
              "description": "Plain-text web search query."
            },
            "count": {
              "type": "integer",
              "description": "Number of search results to return. Defaults to 5 and caps at 10.",
              "default": 5,
              "maximum": 10
            }
          },
          "required": ["query"]
        }
        """);

    private static readonly Regex WhitespacePattern = new(@"\s+", RegexOptions.Compiled);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly BraveSearchSettings _settings;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebSearchTool"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Factory used to resolve the named Brave Search client.</param>
    /// <param name="settings">Resolved Brave Search credential settings.</param>
    public WebSearchTool(IHttpClientFactory httpClientFactory, BraveSearchSettings settings)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(settings);

        this._httpClientFactory = httpClientFactory;
        this._settings = settings;
    }

    /// <summary>
    /// Gets the tool name exposed to the model.
    /// </summary>
    public string Name => "web_search";

    /// <summary>
    /// Gets cached OpenAI-compatible schema for this tool.
    /// </summary>
    public ToolSchema Schema => CachedSchema;

    /// <summary>
    /// Represents arguments for <see cref="WebSearchTool"/>.
    /// </summary>
    /// <param name="Query">The search query.</param>
    /// <param name="Count">The optional result count.</param>
    public sealed record Parameters(string Query, int? Count);

    Task<string> IWorkspaceTool.ExecuteAsync(JsonElement arguments, WorkspaceModel workspace, CancellationToken ct)
    {
        return this.HandleAsync(ToolRegistry.DeserializeArguments<Parameters>(arguments), ct);
    }

    /// <summary>
    /// Executes one Brave Search query and formats the returned results as compact plain text.
    /// </summary>
    /// <param name="parameters">Typed tool arguments.</param>
    /// <param name="ct">The cancellation token for the current tool call.</param>
    /// <returns>A readable result list or a descriptive error string.</returns>
    public async Task<string> HandleAsync(Parameters parameters, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        if (string.IsNullOrWhiteSpace(parameters.Query))
        {
            return "Search error: query is required.";
        }

        if (!this._settings.IsConfigured)
        {
            return "Search error: BRAVE_SEARCH_API_KEY is not configured.";
        }

        var count = parameters.Count ?? DefaultCount;
        if (count <= 0)
        {
            return $"Search error: count must be greater than 0.";
        }

        count = Math.Min(count, MaximumCount);

        var client = this._httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            BuildSearchUri(parameters.Query, count));
        request.Headers.Add("X-Subscription-Token", this._settings.ApiKey);

        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return $"Search failed: {(int)response.StatusCode} {response.ReasonPhrase}";
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: ct).ConfigureAwait(false);
            var results = ParseResults(document.RootElement);

            if (results.Count == 0)
            {
                return $"No results found for: {parameters.Query}";
            }

            return FormatResults(results);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return "Search error: The operation timed out.";
        }
        catch (HttpRequestException ex)
        {
            return $"Search error: {ex.Message}";
        }
        catch (JsonException ex)
        {
            return $"Search error: {ex.Message}";
        }
    }

    private static Uri BuildSearchUri(string query, int count)
    {
        var builder = new UriBuilder(SearchEndpoint)
        {
            Query = $"q={Uri.EscapeDataString(query)}&count={count}"
        };

        return builder.Uri;
    }

    private static IReadOnlyList<SearchResult> ParseResults(JsonElement root)
    {
        if (!root.TryGetProperty("web", out var webElement)
            || !webElement.TryGetProperty("results", out var resultsElement)
            || resultsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var results = new List<SearchResult>();

        foreach (var item in resultsElement.EnumerateArray())
        {
            var title = NormalizeText(TryGetString(item, "title"));
            var url = NormalizeText(TryGetString(item, "url"));
            var snippet = NormalizeText(TryGetString(item, "description"));

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            results.Add(new SearchResult(title, url, snippet));
        }

        return results;
    }

    private static string FormatResults(IReadOnlyList<SearchResult> results)
    {
        var builder = new StringBuilder();

        for (var index = 0; index < results.Count; index++)
        {
            var result = results[index];
            if (index > 0)
            {
                builder.AppendLine();
                builder.AppendLine();
            }

            builder.Append('[');
            builder.Append(index + 1);
            builder.Append("] ");
            builder.AppendLine(result.Title);
            builder.AppendLine(result.Url);
            builder.Append(result.Snippet);
        }

        return builder.ToString();
    }

    private static string TryGetString(JsonElement item, string propertyName)
    {
        return item.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static string NormalizeText(string value)
    {
        return WhitespacePattern.Replace(value, " ").Trim();
    }

    private sealed record SearchResult(string Title, string Url, string Snippet);
}

/// <summary>
/// Represents resolved Brave Search credentials for Codexplorer runtime services.
/// </summary>
/// <param name="ApiKey">The effective API key from environment variables or configuration.</param>
public sealed record BraveSearchSettings(string? ApiKey)
{
    /// <summary>
    /// Gets a value indicating whether a non-empty API key is configured.
    /// </summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(this.ApiKey);
}
