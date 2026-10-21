using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Codexplorer.Automation.AgentComparison.Recording;

/// <summary>Records loopback provider attempts and sends them to a local fake or pinned live endpoint.</summary>
internal sealed class RecordingProxy : IRecordingProxy
{
    private const string RequiredModel = "qwen/qwen3.7-flash";
    private readonly RecordingProxyOptions _options;
    private readonly HttpListener _listener = new();
    private readonly HttpClient? _upstreamClient;
    private readonly ConcurrentDictionary<string, RecordingScope> _scopes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, RecordedRequestKind> _credentialKinds = new(StringComparer.Ordinal);
    private readonly List<RecordedAttempt> _attempts = [];
    private readonly SemaphoreSlim _attemptLock = new(1, 1);
    private readonly object _drainLock = new();
    private TaskCompletionSource _handlersDrained = CreateCompletedSource();
    private CancellationTokenSource? _shutdown;
    private Task? _listenerTask;
    private Uri? _endpoint;
    private long _sequence;
    private string? _captureFailure;

    /// <inheritdoc />
    public string? CaptureFailure => Volatile.Read(ref this._captureFailure);
    private int _activeHandlers;

    /// <summary>Initializes the proxy in a mode with explicit fake or upstream routing.</summary>
    /// <param name="options">The routing, capture, and effective model settings.</param>
    public RecordingProxy(RecordingProxyOptions options)
    {
        this.ValidateOptions(options);
        this._options = options;
        if (options.Mode == RecordingProxyMode.Live)
            this._upstreamClient = options.UpstreamHandler is null
                ? new HttpClient()
                : new HttpClient(options.UpstreamHandler, disposeHandler: true);
    }

    /// <inheritdoc />
    public Uri Endpoint => this._endpoint ?? throw new InvalidOperationException("The recording proxy has not started.");

    /// <inheritdoc />
    public IReadOnlyList<RecordedAttempt> Attempts
    {
        get
        {
            lock (this._attempts)
                return this._attempts.ToArray();
        }
    }

    /// <inheritdoc />
    public ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (this._listenerTask is not null)
            throw new InvalidOperationException("The recording proxy is already started.");

        Directory.CreateDirectory(this._options.CaptureDirectory);
        var port = GetAvailablePort();
        this._endpoint = new Uri($"http://127.0.0.1:{port}/v1/");
        this._listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        this._listener.Start();
        this._shutdown = new CancellationTokenSource();
        this._listenerTask = this.ListenAsync(this._shutdown.Token);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask DrainAsync(CancellationToken cancellationToken = default)
    {
        Task drainTask;
        lock (this._drainLock)
            drainTask = this._activeHandlers == 0 ? Task.CompletedTask : this._handlersDrained.Task;
        await drainTask.WaitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public RecordingScope BeginScope(RecordingScopeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (this._endpoint is null)
            throw new InvalidOperationException("The recording proxy must be started before creating a scope.");
        if (string.IsNullOrWhiteSpace(options.SessionId) || string.IsNullOrWhiteSpace(options.Credential))
            throw new ArgumentException("A session identifier and disposable credential are required.", nameof(options));
        if (options.MaxAgentCalls <= 0 || options.ReservedWrapUpCalls < 0 || options.ReservedWrapUpCalls > options.MaxAgentCalls)
            throw new ArgumentOutOfRangeException(nameof(options));

        RecordingScope? scope = null;
        scope = new RecordingScope(options, this.Endpoint, kind => this.RegisterCategoryCredential(scope!, kind));
        if (!this._scopes.TryAdd(options.Credential, scope))
            throw new InvalidOperationException("The disposable credential is already assigned to an active session.");
        return scope;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (this._shutdown is not null)
        {
            await this._shutdown.CancelAsync();
            this._listener.Close();
            if (this._listenerTask is not null)
            {
                try
                {
                    await this._listenerTask;
                }
                catch (HttpListenerException)
                {
                }
            }
            this._shutdown.Dispose();
        }
        this._upstreamClient?.Dispose();
        this._listener.Close();
        foreach (var scope in this._scopes.Values)
            this._scopes.TryRemove(scope.Credential, out _);
    }

    private static int GetAvailablePort()
    {
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        return ((IPEndPoint)socket.LocalEndpoint).Port;
    }

    private void ValidateOptions(RecordingProxyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Settings.Model != RequiredModel || options.Settings.MaxOutputTokens != 32_768
            || !options.Settings.ReasoningEnabled || string.IsNullOrWhiteSpace(options.Settings.Provider))
            throw new ArgumentException("The proxy requires the pinned model, provider, output limit, and reasoning settings.", nameof(options));
        if (options.Mode == RecordingProxyMode.Fake && (options.FakeProvider is null || options.UpstreamEndpoint is not null
            || options.UpstreamCredential is not null || options.UpstreamHandler is not null || options.AuthorizeForwardAsync is not null))
            throw new ArgumentException("Fake mode requires a local fake and forbids every upstream route.", nameof(options));
        if (options.Mode == RecordingProxyMode.Live && (options.FakeProvider is not null
            || options.UpstreamEndpoint?.AbsoluteUri != "https://openrouter.ai/api/v1/"
            || string.IsNullOrWhiteSpace(options.UpstreamCredential) || options.AuthorizeForwardAsync is null))
            throw new ArgumentException("Live mode requires one authenticated HTTPS upstream and no fake provider.", nameof(options));
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await this._listener.GetContextAsync().WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            this.BeginHandler();
            try
            {
                await this.HandleAsync(context, cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or HttpListenerException or OperationCanceledException
                or ObjectDisposedException)
            {
                context.Response.Abort();
            }
            finally
            {
                this.EndHandler();
            }
        }
    }

    private static TaskCompletionSource CreateCompletedSource()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }

    private void BeginHandler()
    {
        lock (this._drainLock)
        {
            if (this._activeHandlers++ == 0)
                this._handlersDrained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    private void EndHandler()
    {
        lock (this._drainLock)
        {
            if (--this._activeHandlers == 0)
                this._handlersDrained.TrySetResult();
        }
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var credential = context.Request.Headers.Get("Authorization")?.Trim();
        if (credential?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true)
            credential = credential[7..];
        else
            credential = null;

        if (credential is null || !this._scopes.TryGetValue(credential, out var scope))
        {
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            context.Response.Close();
            return;
        }

        var category = this._credentialKinds.TryGetValue(credential, out var authenticatedKind) ? authenticatedKind : (RecordedRequestKind?)null;
        using var lease = await scope.ReserveAsync(cancellationToken, category);
        var startedAt = DateTimeOffset.UtcNow;
        var sequence = Interlocked.Increment(ref this._sequence);
        byte[] requestBody;
        using (var requestStream = new MemoryStream())
        {
            try
            {
                await context.Request.InputStream.CopyToAsync(requestStream, cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or HttpListenerException
                or OperationCanceledException or ObjectDisposedException)
            {
                context.Response.Abort();
                var partialBody = requestStream.ToArray();
                await this.PersistAttemptAsync(new RecordedAttempt(sequence, scope.SessionId, lease.Kind, startedAt,
                    DateTimeOffset.UtcNow, partialBody, partialBody, null, null, GetModel(partialBody), null, null,
                    null, exception.GetType().Name, lease.Rejected, null), CancellationToken.None);
                return;
            }
            requestBody = requestStream.ToArray();
        }

        byte[] effectiveBody;
        try
        {
            effectiveBody = RewriteRequest(requestBody, this._options.Settings);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            var responseBody = Encoding.UTF8.GetBytes("{\"error\":{\"message\":\"invalid provider request\"}}");
            await this.PersistAttemptAsync(new RecordedAttempt(sequence, scope.SessionId, lease.Kind, startedAt,
                DateTimeOffset.UtcNow, requestBody, [], responseBody, 400, null, null, null, null,
                "invalid provider request", true, null), CancellationToken.None);
            await this.WriteResponseAsync(context.Response, HttpStatusCode.BadRequest, "application/json", responseBody, cancellationToken);
            return;
        }
        if (lease.Rejected)
        {
            const string body = "{\"error\":{\"message\":\"recording budget exhausted\",\"type\":\"budget_exhausted\"}}";
            var rejectedBytes = Encoding.UTF8.GetBytes(body);
            try
            {
                await this.WriteResponseAsync(
                    context.Response, HttpStatusCode.TooManyRequests, "application/json", rejectedBytes, cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or HttpListenerException
                or OperationCanceledException or ObjectDisposedException)
            {
                context.Response.Abort();
            }
            await this.PersistAttemptAsync(new RecordedAttempt(sequence, scopeSession(scope), lease.Kind, startedAt,
                DateTimeOffset.UtcNow, requestBody, effectiveBody, rejectedBytes, 429, null, null, null, null,
                "budget exhausted", true, null), cancellationToken);
            return;
        }

        var requestPath = context.Request.Url!.AbsolutePath;
        var routeStatus = requestPath != "/v1/chat/completions" || context.Request.Url.Query.Length > 0
            ? HttpStatusCode.NotFound
            : context.Request.HttpMethod != "POST" ? HttpStatusCode.MethodNotAllowed : (HttpStatusCode?)null;
        if (routeStatus.HasValue)
        {
            var errorBody = Encoding.UTF8.GetBytes("{\"error\":{\"message\":\"unsupported provider route\"}}");
            try
            {
                await this.WriteResponseAsync(context.Response, routeStatus.Value, "application/json", errorBody, cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or HttpListenerException
                or OperationCanceledException or ObjectDisposedException)
            {
                context.Response.Abort();
            }
            await this.PersistAttemptAsync(new RecordedAttempt(sequence, scope.SessionId, lease.Kind, startedAt,
                DateTimeOffset.UtcNow, requestBody, effectiveBody, errorBody, (int)routeStatus.Value,
                GetModel(effectiveBody), null, null, null, "unsupported provider route", false, null), cancellationToken);
            return;
        }

        using var capturedResponse = new MemoryStream();
        RecordedUpstreamResponse? providerResponse = null;
        int? clientStatus = null;
        var responseStarted = false;
        try
        {
            providerResponse = await this.SendAsync(context.Request, effectiveBody, cancellationToken);
            clientStatus = (int)providerResponse.Status;
            WriteResponseHeaders(context.Response, providerResponse);
            responseStarted = true;
            await PumpResponseAsync(providerResponse.Body, context.Response.OutputStream, capturedResponse, cancellationToken);
            context.Response.Close();
            var responseBody = capturedResponse.ToArray();
            var parsed = RecordedUsageParser.Parse(responseBody, providerResponse.ContentType);
            await this.PersistAttemptAsync(new RecordedAttempt(sequence, scopeSession(scope), lease.Kind, startedAt,
                DateTimeOffset.UtcNow, requestBody, effectiveBody, responseBody, clientStatus, GetModel(effectiveBody),
                parsed.GenerationId, parsed.Usage, parsed.RawJson,
                (int)providerResponse.Status >= 400 ? $"HTTP {(int)providerResponse.Status}" : null,
                false, null), cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException
            or OperationCanceledException or HttpListenerException or ObjectDisposedException or InvalidOperationException
            or ArgumentException)
        {
            if (responseStarted || (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
                || exception is IOException or HttpListenerException or ObjectDisposedException)
                context.Response.Abort();
            else
            {
                var errorBody = Encoding.UTF8.GetBytes("{\"error\":{\"message\":\"recording provider failure\"}}");
                clientStatus = (int)HttpStatusCode.BadGateway;
                await this.WriteResponseAsync(
                    context.Response, HttpStatusCode.BadGateway, "application/json", errorBody, CancellationToken.None);
                await capturedResponse.WriteAsync(errorBody);
            }
            var responseBody = capturedResponse.Length == 0 ? null : capturedResponse.ToArray();
            var contentType = providerResponse?.ContentType ?? "application/json";
            var parsed = responseBody is null ? (Usage: (RecordedUsage?)null, RawJson: (string?)null, GenerationId: (string?)null)
                : RecordedUsageParser.Parse(responseBody, contentType);
            await this.PersistAttemptAsync(new RecordedAttempt(sequence, scopeSession(scope), lease.Kind, startedAt,
                DateTimeOffset.UtcNow, requestBody, effectiveBody, responseBody, clientStatus, GetModel(effectiveBody),
                parsed.GenerationId, parsed.Usage, parsed.RawJson, exception.GetType().Name, false, null), CancellationToken.None);
        }
        finally
        {
            if (providerResponse is not null)
                await providerResponse.DisposeAsync();
        }
    }

    private async Task<RecordedUpstreamResponse> SendAsync(
        HttpListenerRequest incoming, byte[] effectiveBody, CancellationToken cancellationToken)
    {
        if (this.CaptureFailure is not null)
            throw new InvalidOperationException("Capture persistence failed; further provider requests are refused.");
        if (this._options.Mode == RecordingProxyMode.Fake)
        {
            var fake = this._options.FakeProvider!.Respond(effectiveBody);
            return RecordedUpstreamResponse.FromBytes(fake.Status, fake.ContentType, fake.Body);
        }

        if (!await this._options.AuthorizeForwardAsync!(cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("Readiness changed before forwarding the recorded request.");

        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(this._options.UpstreamEndpoint!, "chat/completions"));
        message.Content = new ByteArrayContent(effectiveBody);
        message.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", this._options.UpstreamCredential);
        if (incoming.Headers.Get("Accept") is { Length: > 0 } accept)
            message.Headers.TryAddWithoutValidation("Accept", accept);
        var response = await this._upstreamClient!.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        return new RecordedUpstreamResponse(response.StatusCode,
            response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream",
            response.Content.Headers.ContentLength,
            body,
            response);
    }

    /// <summary>Enforces the shared provider contract before an attempt can cross the upstream boundary.</summary>
    /// <param name="requestBody">The exact child-submitted JSON body.</param>
    /// <param name="settings">The immutable shared model, route and output settings.</param>
    /// <returns>The effective request body with native messages and tools preserved.</returns>
    /// <exception cref="JsonException">The request is not a supported chat-completion object.</exception>
    private static byte[] RewriteRequest(byte[] requestBody, ComparisonSettings settings)
    {
        var root = JsonNode.Parse(requestBody) as JsonObject ?? throw new JsonException("A provider request must be a JSON object.");
        if (root["messages"] is not JsonArray messages || messages.Count == 0
            || messages.Any(message => message is not JsonObject || message["role"] is not JsonValue))
            throw new JsonException("A provider request must contain valid messages.");
        root["model"] = settings.Model;
        root.Remove("models");
        root.Remove("max_completion_tokens");
        root.Remove("reasoning_effort");
        root.Remove("include_reasoning");
        root["max_tokens"] = settings.MaxOutputTokens;
        root["reasoning"] = new JsonObject { ["enabled"] = settings.ReasoningEnabled };
        if (root["stream"]?.GetValue<bool>() == true)
        {
            var streamOptions = root["stream_options"] as JsonObject ?? new JsonObject();
            streamOptions["include_usage"] = true;
            root["stream_options"] = streamOptions;
        }
        foreach (var sampling in new[]
        {
            "temperature", "top_p", "top_k", "top_a", "min_p", "frequency_penalty", "presence_penalty",
            "repetition_penalty", "seed", "logit_bias"
        })
            root.Remove(sampling);
        root["provider"] = new JsonObject
        {
            ["only"] = new JsonArray(JsonValue.Create(settings.Provider)),
            ["order"] = new JsonArray(JsonValue.Create(settings.Provider)),
            ["allow_fallbacks"] = false,
            ["require_parameters"] = true,
        };
        root["transforms"] = new JsonArray();
        return Encoding.UTF8.GetBytes(root.ToJsonString());
    }

    private async Task WriteResponseAsync(
        HttpListenerResponse response, HttpStatusCode status, string contentType, byte[] body, CancellationToken cancellationToken)
    {
        response.StatusCode = (int)status;
        response.ContentType = contentType;
        response.ContentLength64 = body.Length;
        await response.OutputStream.WriteAsync(body, cancellationToken);
        response.Close();
    }

    private static void WriteResponseHeaders(HttpListenerResponse response, RecordedUpstreamResponse upstream)
    {
        response.StatusCode = (int)upstream.Status;
        response.ContentType = upstream.ContentType;
        if (upstream.ContentLength.HasValue)
            response.ContentLength64 = upstream.ContentLength.Value;
        else
            response.SendChunked = true;
    }

    private static async Task PumpResponseAsync(
        Stream upstream, Stream downstream, MemoryStream captured, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var bytesRead = await upstream.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
                return;
            await captured.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            await downstream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            await downstream.FlushAsync(cancellationToken);
        }
    }

    private async Task PersistAttemptAsync(RecordedAttempt attempt, CancellationToken cancellationToken)
    {
        await this._attemptLock.WaitAsync(cancellationToken);
        try
        {
            var attemptDirectory = Path.Combine(this._options.CaptureDirectory, "attempts", attempt.Sequence.ToString("D6"));
            Directory.CreateDirectory(attemptDirectory);
            var requestClient = Path.Combine(attemptDirectory, "request-client.json");
            var requestEffective = Path.Combine(attemptDirectory, "request-upstream.json");
            await File.WriteAllBytesAsync(requestClient, attempt.RequestBody, cancellationToken);
            await File.WriteAllBytesAsync(requestEffective, attempt.EffectiveBody, cancellationToken);
            var normalizationPath = Path.Combine(attemptDirectory, "normalization.json");
            await File.WriteAllTextAsync(normalizationPath,
                JsonSerializer.Serialize(DescribeNormalization(attempt.RequestBody, attempt.EffectiveBody)), cancellationToken);
            string? responsePath = null;
            if (attempt.ResponseBody is not null)
            {
                responsePath = Path.Combine(attemptDirectory, GetResponseFileName(attempt));
                await File.WriteAllBytesAsync(responsePath, attempt.ResponseBody, cancellationToken);
            }

            var files = new RecordedAttemptFiles(
                Path.GetRelativePath(this._options.CaptureDirectory, requestClient).Replace(Path.DirectorySeparatorChar, '/'),
                Path.GetRelativePath(this._options.CaptureDirectory, requestEffective).Replace(Path.DirectorySeparatorChar, '/'),
                responsePath is null ? null : Path.GetRelativePath(this._options.CaptureDirectory, responsePath)
                    .Replace(Path.DirectorySeparatorChar, '/'))
            {
                Normalization = Path.GetRelativePath(this._options.CaptureDirectory, normalizationPath).Replace(Path.DirectorySeparatorChar, '/')
            };
            var persisted = attempt with
            {
                CaptureFiles = files, RequestedModel = GetModel(attempt.RequestBody),
                ReturnedModel = ResponseMetadata(attempt.ResponseBody, "model"),
                ReturnedProvider = ResponseMetadata(attempt.ResponseBody, "provider"),
                Tools = RecordedRequestDiagnostics.Read(attempt.EffectiveBody)
            };
            var file = Path.Combine(this._options.CaptureDirectory, "attempts.jsonl");
            var line = JsonSerializer.Serialize(persisted, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            await File.AppendAllTextAsync(file, line + Environment.NewLine, cancellationToken);
            lock (this._attempts)
                this._attempts.Add(persisted);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Interlocked.CompareExchange(ref this._captureFailure, exception.GetType().Name, null);
            var incomplete = attempt with
            {
                Error = "Capture persistence failed: " + exception.GetType().Name,
                Usage = attempt.Usage is { } usage ? usage with { Complete = false } : null
            };
            lock (this._attempts)
                this._attempts.Add(incomplete);
        }
        finally
        {
            this._attemptLock.Release();
        }
    }

    private static string GetResponseFileName(RecordedAttempt attempt)
    {
        if (attempt.ResponseBody is null)
            return "response.bin";
        if (attempt.ResponseBody.AsSpan().StartsWith("data:"u8) || attempt.ResponseBody.AsSpan().StartsWith("event:"u8))
            return "response.sse";
        return "response.json";
    }

    private static string scopeSession(RecordingScope scope) => scope.SessionId;

    private static IReadOnlyList<RequestNormalizationField> DescribeNormalization(byte[] submitted, byte[] effective)
    {
        try
        {
            using var source = JsonDocument.Parse(submitted);
            using var destination = JsonDocument.Parse(effective);
            var fields = source.RootElement.EnumerateObject().Select(property => property.Name)
                .Union(destination.RootElement.EnumerateObject().Select(property => property.Name), StringComparer.Ordinal);
            var changes = new List<RequestNormalizationField>();
            foreach (var field in fields)
            {
                JsonElement? before = source.RootElement.TryGetProperty(field, out var original) ? original.Clone() : null;
                JsonElement? after = destination.RootElement.TryGetProperty(field, out var normalized) ? normalized.Clone() : null;
                if (before?.GetRawText() != after?.GetRawText())
                    changes.Add(new RequestNormalizationField(field, before, after));
            }
            return changes;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return [];
        }
    }

    private static string? ResponseMetadata(byte[]? body, string field)
    {
        if (body is null)
            return null;
        var text = Encoding.UTF8.GetString(body);
        var frames = text.TrimStart().StartsWith('{') ? new[] { text }
            : text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
                .Where(line => line.StartsWith("data:", StringComparison.Ordinal)).Select(line => line[5..].Trim());
        string? result = null;
        foreach (var frame in frames)
        {
            try
            {
                using var document = JsonDocument.Parse(frame);
                if (document.RootElement.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String)
                    result = value.GetString();
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException)
            {
            }
        }
        return result;
    }

    private string RegisterCategoryCredential(RecordingScope scope, RecordedRequestKind kind)
    {
        var credential = "overhead-" + Guid.NewGuid().ToString("N");
        this._scopes[credential] = scope;
        this._credentialKinds[credential] = kind;
        return credential;
    }

    private static string? GetModel(byte[] body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("model", out var model) ? model.GetString() : null;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private sealed class RecordedUpstreamResponse(
        HttpStatusCode status,
        string contentType,
        long? contentLength,
        Stream body,
        HttpResponseMessage? response) : IAsyncDisposable
    {
        /// <summary>Gets the provider response status.</summary>
        public HttpStatusCode Status { get; } = status;

        /// <summary>Gets the provider response media type.</summary>
        public string ContentType { get; } = contentType;

        /// <summary>Gets the response length when the provider supplied it.</summary>
        public long? ContentLength { get; } = contentLength;

        /// <summary>Gets the provider response stream.</summary>
        public Stream Body { get; } = body;

        /// <summary>Creates an in-memory response for the local scripted provider.</summary>
        /// <param name="status">The synthetic provider status.</param>
        /// <param name="contentType">The synthetic media type.</param>
        /// <param name="body">The exact synthetic response bytes.</param>
        /// <returns>The response wrapper consumed by the recorder.</returns>
        public static RecordedUpstreamResponse FromBytes(HttpStatusCode status, string contentType, byte[] body)
        {
            var stream = new MemoryStream(body, writable: false);
            return new RecordedUpstreamResponse(status, contentType, body.LongLength, stream, null);
        }

        /// <summary>Closes the response stream and owned HTTP response.</summary>
        /// <returns>A task that represents the asynchronous disposal.</returns>
        public async ValueTask DisposeAsync()
        {
            await this.Body.DisposeAsync();
            response?.Dispose();
        }
    }
}
