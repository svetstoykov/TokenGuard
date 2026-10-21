using OpenAI;
using OpenAI.Chat;

namespace Codexplorer.Configuration;

internal static class OpenRouterChatClientFactory
{
    private const int MaxRetries = 10;

    private static readonly Uri OpenRouterEndpoint = new("https://openrouter.ai/api/v1");
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    /// <summary>Creates a provider client using the validated internal benchmark route when supplied.</summary>
    /// <param name="options">The effective sample model and credential settings.</param>
    /// <param name="benchmarkSummarizer">Whether a benchmark summary credential identifies this client's requests.</param>
    /// <returns>The configured provider client.</returns>
    /// <exception cref="InvalidOperationException">Required settings or a valid loopback override are unavailable.</exception>
    internal static ChatClient Create(CodexplorerOptions options, bool benchmarkSummarizer = false)
    {
        ArgumentNullException.ThrowIfNull(options);

        var openRouterOptions = options.OpenRouter
            ?? throw new InvalidOperationException("Codexplorer OpenRouter options are not configured.");
        var modelOptions = options.Model
            ?? throw new InvalidOperationException("Codexplorer model options are not configured.");
        var apiKey = openRouterOptions.ApiKey;
        if (benchmarkSummarizer && Environment.GetEnvironmentVariable("CODEXPLORER_BENCHMARK_OPENROUTER_BASE_URL") is not null
            && Environment.GetEnvironmentVariable("CODEXPLORER_BENCHMARK_SUMMARIZER_API_KEY") is { Length: > 0 } summaryCredential)
            apiKey = summaryCredential;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Codexplorer OpenRouter API key is not configured.");
        }

        var modelName = modelOptions.Name
            ?? throw new InvalidOperationException("Codexplorer model name is not configured.");
        var client = new OpenAIClient(
            new System.ClientModel.ApiKeyCredential(apiKey),
            new OpenAIClientOptions
            {
                Endpoint = ResolveEndpoint(Environment.GetEnvironmentVariable("CODEXPLORER_BENCHMARK_OPENROUTER_BASE_URL")),
                RetryPolicy = new FixedDelayRetryPolicy(MaxRetries, RetryDelay)
            });

        return client.GetChatClient(modelName);
    }

    /// <summary>Resolves the internal loopback benchmark endpoint or the default OpenRouter endpoint.</summary>
    /// <param name="benchmarkOverride">The loopback HTTP base URL, or null for the default endpoint.</param>
    /// <returns>The validated client endpoint.</returns>
    /// <exception cref="InvalidOperationException">The override is not an uncredentialed loopback HTTP address.</exception>
    internal static Uri ResolveEndpoint(string? benchmarkOverride)
    {
        if (benchmarkOverride is null)
            return OpenRouterEndpoint;
        if (!Uri.TryCreate(benchmarkOverride, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme != Uri.UriSchemeHttp || !endpoint.IsLoopback
            || !string.IsNullOrEmpty(endpoint.UserInfo) || !string.IsNullOrEmpty(endpoint.Query)
            || !string.IsNullOrEmpty(endpoint.Fragment))
            throw new InvalidOperationException("The benchmark endpoint must be an uncredentialed loopback HTTP address.");
        return endpoint;
    }
}
