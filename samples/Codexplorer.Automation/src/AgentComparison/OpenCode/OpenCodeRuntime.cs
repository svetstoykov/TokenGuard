using System.Diagnostics;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Codexplorer.Automation.AgentComparison.Recording;
using Codexplorer.Automation.Runner;

namespace Codexplorer.Automation.AgentComparison.OpenCode;

/// <summary>Runs one isolated task through the pinned OpenCode server process.</summary>
internal sealed class OpenCodeRuntime(IRecordingScope recording, IComparisonHelper helper) : IComparisonRuntime
{
    private const string ProviderId = "openrouter";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IRecordingScope _recording = recording;
    private readonly IComparisonHelper _helper = helper;

    /// <inheritdoc />
    public async Task<RuntimeResult> RunAsync(RuntimeInvocation invocation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        if (this._recording.Endpoint.Scheme != Uri.UriSchemeHttp || !this._recording.Endpoint.IsLoopback
            || invocation.RecorderEndpoint != this._recording.Endpoint
            || !string.Equals(invocation.LocalCredential, this._recording.Credential, StringComparison.Ordinal))
            throw new InvalidOperationException("OpenCode requires the exact loopback recording scope assigned to this session.");
        var artifacts = OpenCodePathGuard.PrepareArtifactRoot(invocation.Artifacts);
        var stateRoot = OpenCodePathGuard.PrepareIsolatedRoot(invocation.StateRoot, "runtime state");
        var checkout = Path.GetFullPath(invocation.Checkout);
        var sessionId = SanitizeSessionId(invocation.Session.Id);
        var eventPath = OpenCodePathGuard.ChildFile(artifacts, $"{sessionId}-events.jsonl");
        var exportPath = OpenCodePathGuard.ChildFile(artifacts, $"{sessionId}-export.json");
        var exportErrorPath = OpenCodePathGuard.ChildFile(artifacts, $"{sessionId}-export-error.json");
        var apiErrorPath = OpenCodePathGuard.ChildFile(artifacts, $"{sessionId}-api-error.json");
        var promptResponsesPath = OpenCodePathGuard.ChildFile(artifacts, $"{sessionId}-prompt-responses.jsonl");
        var resolvedPromptPath = OpenCodePathGuard.ChildFile(artifacts, $"{sessionId}-resolved-prompt.json");
        var inventoryPath = OpenCodePathGuard.ChildFile(artifacts, $"{sessionId}-environment.json");
        var effectiveConfigPath = OpenCodePathGuard.ChildFile(artifacts, $"{sessionId}-effective-config.json");
        var toolInventoryPath = OpenCodePathGuard.ChildFile(artifacts, $"{sessionId}-tool-inventory.json");
        var logPath = OpenCodePathGuard.ChildFile(artifacts, $"{sessionId}-process.log");
        var capturePaths = new List<string>();
        Process? server = null;
        HttpClient? client = null;
        OpenCodeEventCapture? eventCapture = null;
        IReadOnlyDictionary<string, string>? launchEnvironment = null;
        var serverOutput = new StringBuilder();
        Task<string>? serverErrorOutput = null;
        string? opencodeSessionId = null;
        string? finalAnswer = null;
        string? error = null;
        var outcome = "failed";
        var partial = true;

        try
        {
            var resolvedPrompt = ResolveArtifactPath(invocation.Session.Prompt, artifacts);
            await File.WriteAllTextAsync(
                resolvedPromptPath,
                JsonSerializer.Serialize(new
                {
                    templatePromptSha256 = invocation.Session.AdaptedPromptSha256,
                    templatePrompt = invocation.Session.Prompt,
                    resolvedPromptSha256 = Sha256(resolvedPrompt),
                    resolvedPrompt,
                }, JsonOptions),
                cancellationToken).ConfigureAwait(false);
            capturePaths.Add(RelativePath(artifacts, resolvedPromptPath));

            var launch = OpenCodeProcessConfiguration.Prepare(
                invocation,
                checkout,
                artifacts,
                stateRoot,
                this._recording.Endpoint,
                this._recording.Credential);
            launchEnvironment = launch.Environment;
            await File.WriteAllTextAsync(
                inventoryPath,
                JsonSerializer.Serialize(launch.Inventory, JsonOptions),
                cancellationToken).ConfigureAwait(false);
            capturePaths.Add(RelativePath(artifacts, inventoryPath));

            server = StartServer(invocation.Executable, checkout, launch.Environment);
            serverErrorOutput = server.StandardError.ReadToEndAsync();
            var serverUrl = await WaitForServerAsync(server, serverOutput, cancellationToken).ConfigureAwait(false);
            client = CreateClient(serverUrl, checkout);
            await CaptureEffectiveConfigurationAsync(
                client,
                checkout,
                effectiveConfigPath,
                toolInventoryPath,
                invocation.LocalCredential,
                cancellationToken).ConfigureAwait(false);
            capturePaths.Add(RelativePath(artifacts, effectiveConfigPath));
            capturePaths.Add(RelativePath(artifacts, toolInventoryPath));

            using var sessionResponse = await client.PostAsync(
                $"/session?directory={Uri.EscapeDataString(checkout)}",
                JsonContent(new
                {
                    title = $"TokenGuard {invocation.Session.Id}",
                    model = new { providerID = ProviderId, id = invocation.Settings.Model },
                    permission = OpenCodeProcessConfiguration.CreateSessionPermissions(artifacts),
                }),
                cancellationToken).ConfigureAwait(false);
            var sessionBody = await sessionResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!sessionResponse.IsSuccessStatusCode)
            {
                var errorDocument = JsonSerializer.Serialize(new
                {
                    operation = "create-session",
                    statusCode = (int)sessionResponse.StatusCode,
                    reasonPhrase = sessionResponse.ReasonPhrase,
                    body = Redact(sessionBody, invocation.LocalCredential),
                }, JsonOptions);
                await File.WriteAllTextAsync(apiErrorPath, errorDocument, cancellationToken).ConfigureAwait(false);
                capturePaths.Add(RelativePath(artifacts, apiErrorPath));
                throw new InvalidDataException(
                    $"OpenCode session creation failed with HTTP {(int)sessionResponse.StatusCode}: {Redact(sessionBody, invocation.LocalCredential)}");
            }
            opencodeSessionId = ReadString(sessionBody, "id")
                ?? throw new InvalidDataException("OpenCode did not return a session id.");

            eventCapture = OpenCodeEventCapture.Start(client, opencodeSessionId, checkout, eventPath);
            var mainPrompt = resolvedPrompt;
            var currentAnswer = await RunPromptAsync(
                invocation,
                client,
                opencodeSessionId,
                mainPrompt,
                cancellationToken,
                promptResponsesPath,
                invocation.LocalCredential).ConfigureAwait(false);
            finalAnswer = currentAnswer.Text;

            while (!cancellationToken.IsCancellationRequested && !this._recording.MainBudgetExhausted.IsCancellationRequested)
            {
                var question = ReadRunnerQuestion(finalAnswer);
                string nextPrompt;
                if (question is not null)
                {
                    nextPrompt = await this._helper.AnswerAsync(
                        invocation,
                        question,
                        finalAnswer,
                        this._recording.MainCallsConsumed,
                        wrapUpSent: false,
                        cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    nextPrompt = currentAnswer.MaxTokensReached
                        ? AutomationRunnerPrompts.CreateResumePrompt(
                            Math.Max(0, invocation.Session.MaxAgentCalls - invocation.Session.WrapUpCalls - this._recording.MainCallsConsumed))
                        : AutomationRunnerPrompts.CreateContinuationPrompt(
                            Math.Max(0, invocation.Session.MaxAgentCalls - invocation.Session.WrapUpCalls - this._recording.MainCallsConsumed));
                }

                currentAnswer = await RunPromptAsync(
                    invocation,
                    client,
                    opencodeSessionId,
                    nextPrompt,
                    cancellationToken,
                    promptResponsesPath,
                    invocation.LocalCredential).ConfigureAwait(false);
                finalAnswer = currentAnswer.Text;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                outcome = "cancelled";
                error = "The comparison session was cancelled.";
            }
            else if (this._recording.MainBudgetExhausted.IsCancellationRequested)
            {
                if (!currentAnswer.AbortConfirmed)
                {
                    outcome = "budgetExhausted";
                    partial = true;
                    error = currentAnswer.Error ?? "OpenCode did not confirm cancellation of the exhausted main-agent request.";
                }
                else
                {
                    this._recording.BeginWrapUp();
                    try
                    {
                        var wrapUp = await RunPromptAsync(
                            invocation,
                            client,
                            opencodeSessionId,
                            AutomationRunnerPrompts.CreateWrapUpPrompt(),
                            cancellationToken,
                            promptResponsesPath,
                            invocation.LocalCredential,
                            monitorMainBudget: false).ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(wrapUp.Text))
                            finalAnswer = wrapUp.Text;
                        partial = string.IsNullOrWhiteSpace(finalAnswer);
                        outcome = partial ? "budgetExhausted" : "completed";
                    }
                    catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or InvalidDataException)
                    {
                        outcome = cancellationToken.IsCancellationRequested ? "cancelled" : "budgetExhausted";
                        partial = true;
                        error = Redact(exception.Message, invocation.LocalCredential);
                    }
                }
            }
            else
            {
                outcome = "completed";
                partial = false;
            }

        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = "cancelled";
            partial = true;
            error = "The comparison session was cancelled.";
        }
        catch (OpenCodePromptFailureException exception)
        {
            outcome = "failed";
            partial = true;
            error = Redact(exception.Message, invocation.LocalCredential);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or HttpRequestException
            or InvalidDataException or JsonException or UnauthorizedAccessException or ArgumentException or TimeoutException)
        {
            outcome = this._recording.MainBudgetExhausted.IsCancellationRequested ? "budgetExhausted" : "failed";
            partial = true;
            error = Redact(exception.Message, invocation.LocalCredential);
        }
        finally
        {
            if (eventCapture is not null)
                await eventCapture.DisposeAsync().ConfigureAwait(false);
            client?.Dispose();
            if (server is not null)
                await StopServerAsync(server).ConfigureAwait(false);
            if (opencodeSessionId is not null)
                await ExportSessionAsync(invocation, launchEnvironment, opencodeSessionId, exportPath, exportErrorPath)
                    .ConfigureAwait(false);
            if (server is not null)
            {
                var errorOutput = serverErrorOutput is null ? string.Empty : await serverErrorOutput.ConfigureAwait(false);
                await WriteProcessLogAsync(logPath, serverOutput, errorOutput, invocation.LocalCredential).ConfigureAwait(false);
            }
        }

        if (File.Exists(eventPath))
            capturePaths.Add(RelativePath(artifacts, eventPath));
        if (File.Exists(exportPath))
            capturePaths.Add(RelativePath(artifacts, exportPath));
        if (File.Exists(exportErrorPath))
            capturePaths.Add(RelativePath(artifacts, exportErrorPath));
        if (File.Exists(apiErrorPath) && !capturePaths.Contains(RelativePath(artifacts, apiErrorPath), StringComparer.Ordinal))
            capturePaths.Add(RelativePath(artifacts, apiErrorPath));
        if (File.Exists(promptResponsesPath))
            capturePaths.Add(RelativePath(artifacts, promptResponsesPath));
        if (File.Exists(logPath) && !capturePaths.Contains(RelativePath(artifacts, logPath), StringComparer.Ordinal))
            capturePaths.Add(RelativePath(artifacts, logPath));

        return new RuntimeResult(
            outcome,
            partial,
            finalAnswer,
            this._recording.AgentCallsConsumed,
            capturePaths.Distinct(StringComparer.Ordinal).ToArray(),
            error);
    }

    private static Process StartServer(string executable, string checkout, IReadOnlyDictionary<string, string> environment)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = checkout,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("serve");
        startInfo.ArgumentList.Add("--hostname");
        startInfo.ArgumentList.Add("127.0.0.1");
        startInfo.ArgumentList.Add("--port");
        startInfo.ArgumentList.Add("0");
        startInfo.Environment.Clear();
        foreach (var pair in environment)
            startInfo.Environment[pair.Key] = pair.Value;

        return Process.Start(startInfo) ?? throw new InvalidOperationException("OpenCode server did not start.");
    }

    private static async Task<Uri> WaitForServerAsync(Process server, StringBuilder output, CancellationToken cancellationToken)
    {
        var wait = Stopwatch.StartNew();
        var nextLine = server.StandardOutput.ReadLineAsync();
        while (!server.HasExited && wait.Elapsed < TimeSpan.FromSeconds(30))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var completed = await Task.WhenAny(nextLine, Task.Delay(50, cancellationToken)).ConfigureAwait(false);
            if (completed == nextLine)
            {
                var text = await nextLine.ConfigureAwait(false);
                if (text is null)
                    break;
                output.AppendLine(text);
                var marker = "opencode server listening on ";
                var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (index >= 0 && Uri.TryCreate(text[(index + marker.Length)..].Trim(), UriKind.Absolute, out var url))
                    return url;
                nextLine = server.StandardOutput.ReadLineAsync();
            }
        }

        if (server.HasExited)
            throw new InvalidOperationException($"OpenCode server exited with code {server.ExitCode}.");
        throw new TimeoutException("OpenCode server did not report its loopback endpoint.");
    }

    private static HttpClient CreateClient(Uri serverUrl, string checkout)
    {
        var client = new HttpClient { BaseAddress = serverUrl, Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.Add("x-opencode-directory", checkout);
        return client;
    }

    private static async Task CaptureEffectiveConfigurationAsync(
        HttpClient client,
        string checkout,
        string configPath,
        string toolsPath,
        string credential,
        CancellationToken cancellationToken)
    {
        var directory = Uri.EscapeDataString(checkout);
        using var configResponse = await client.GetAsync($"/config?directory={directory}", cancellationToken).ConfigureAwait(false);
        var config = await configResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        configResponse.EnsureSuccessStatusCode();
        await File.WriteAllTextAsync(configPath, Redact(config, credential), cancellationToken).ConfigureAwait(false);

        using var toolsResponse = await client.GetAsync(
            $"/experimental/tool/ids?directory={directory}",
            cancellationToken).ConfigureAwait(false);
        var tools = await toolsResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        toolsResponse.EnsureSuccessStatusCode();
        await File.WriteAllTextAsync(toolsPath, Redact(tools, credential), cancellationToken).ConfigureAwait(false);
    }

    private async Task<OpenCodePromptResult> RunPromptAsync(
        RuntimeInvocation invocation,
        HttpClient client,
        string sessionId,
        string prompt,
        CancellationToken cancellationToken,
        string responseCapturePath,
        string credential,
        bool monitorMainBudget = true)
    {
        using var promptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var mainBudgetRegistration = monitorMainBudget
            ? this._recording.MainBudgetExhausted.Register(promptCancellation.Cancel)
            : default;
        try
        {
            using var response = await client.PostAsync(
                $"/session/{Uri.EscapeDataString(sessionId)}/message?directory={Uri.EscapeDataString(invocation.Checkout)}",
                JsonContent(new
                {
                    model = new { providerID = ProviderId, modelID = invocation.Settings.Model },
                    parts = new[] { new { type = "text", text = prompt } },
                }),
                promptCancellation.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(promptCancellation.Token).ConfigureAwait(false);
            await File.AppendAllTextAsync(
                responseCapturePath,
                JsonSerializer.Serialize(new
                {
                    statusCode = (int)response.StatusCode,
                    reasonPhrase = response.ReasonPhrase,
                    body = Redact(body, credential),
                }, JsonOptions) + Environment.NewLine,
                CancellationToken.None).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var assistantError = ReadAssistantError(body);
            if (assistantError is not null)
                throw new OpenCodePromptFailureException(assistantError);
            var text = ReadAssistantText(body);
            var maxTokensReached = ReadFinish(body) == "length";
            return new OpenCodePromptResult(text, maxTokensReached, AbortConfirmed: true);
        }
        catch (OperationCanceledException) when (this._recording.MainBudgetExhausted.IsCancellationRequested
            && monitorMainBudget && !cancellationToken.IsCancellationRequested)
        {
            var abort = await AbortSessionAsync(client, sessionId, invocation.Checkout, cancellationToken,
                responseCapturePath, credential).ConfigureAwait(false);
            return new OpenCodePromptResult(string.Empty, false, abort.Confirmed, abort.Error);
        }
        catch (HttpRequestException) when (this._recording.MainBudgetExhausted.IsCancellationRequested
            && monitorMainBudget && !cancellationToken.IsCancellationRequested)
        {
            var abort = await AbortSessionAsync(client, sessionId, invocation.Checkout, cancellationToken,
                responseCapturePath, credential).ConfigureAwait(false);
            return new OpenCodePromptResult(string.Empty, false, abort.Confirmed, abort.Error);
        }
    }

    private static async Task<OpenCodeAbortResult> AbortSessionAsync(
        HttpClient client,
        string sessionId,
        string checkout,
        CancellationToken cancellationToken,
        string responseCapturePath,
        string credential)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            using var response = await client.PostAsync(
                $"/session/{Uri.EscapeDataString(sessionId)}/abort?directory={Uri.EscapeDataString(checkout)}",
                content: null,
                bounded.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(bounded.Token).ConfigureAwait(false);
            await File.AppendAllTextAsync(
                responseCapturePath,
                JsonSerializer.Serialize(new
                {
                    operation = "abort-session",
                    statusCode = (int)response.StatusCode,
                    reasonPhrase = response.ReasonPhrase,
                    body = Redact(body, credential),
                }, JsonOptions) + Environment.NewLine,
                CancellationToken.None).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.True)
                return new OpenCodeAbortResult(true, null);
            return new OpenCodeAbortResult(false, "OpenCode returned false when asked to abort the exhausted main-agent request.");
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or IOException
            or JsonException or InvalidOperationException)
        {
            return new OpenCodeAbortResult(false, $"OpenCode session abort failed: {Redact(exception.Message, credential)}");
        }
    }

    private static async Task ExportSessionAsync(
        RuntimeInvocation invocation,
        IReadOnlyDictionary<string, string>? launchEnvironment,
        string sessionId,
        string exportPath,
        string exportErrorPath)
    {
        try
        {
            var startInfo = new ProcessStartInfo(invocation.Executable)
            {
                WorkingDirectory = invocation.Checkout,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("export");
            startInfo.ArgumentList.Add(sessionId);
            startInfo.Environment.Clear();
            if (launchEnvironment is not null)
            {
                foreach (var pair in launchEnvironment)
                    startInfo.Environment[pair.Key] = pair.Value;
            }

            using var process = Process.Start(startInfo);
            if (process is null)
                return;
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);
            var errorOutput = await errorTask.ConfigureAwait(false);
            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                await File.WriteAllTextAsync(exportPath, Redact(output, invocation.LocalCredential)).ConfigureAwait(false);
            else
                await File.WriteAllTextAsync(
                    exportErrorPath,
                    Redact(JsonSerializer.Serialize(new { exitCode = process.ExitCode, stdout = output, stderr = errorOutput }),
                        invocation.LocalCredential))
                    .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or OperationCanceledException
            or UnauthorizedAccessException or Win32Exception or TimeoutException)
        {
            try
            {
                await File.WriteAllTextAsync(
                    exportErrorPath,
                    Redact(JsonSerializer.Serialize(new { error = exception.GetType().Name }), invocation.LocalCredential))
                    .ConfigureAwait(false);
            }
            catch (Exception writeException) when (writeException is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static async Task WriteProcessLogAsync(
        string path,
        StringBuilder output,
        string errorOutput,
        string credential)
    {
        var details = new StringBuilder(output.ToString());
        if (!string.IsNullOrWhiteSpace(errorOutput))
            details.AppendLine(errorOutput);
        await File.WriteAllTextAsync(path, Redact(details.ToString(), credential)).ConfigureAwait(false);
    }

    private static async Task StopServerAsync(Process server)
    {
        try
        {
            if (!server.HasExited)
                server.Kill(entireProcessTree: true);
            await server.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or OperationCanceledException)
        {
        }
        finally
        {
            server.Dispose();
        }
    }

    private static StringContent JsonContent<T>(T value)
    {
        return new StringContent(JsonSerializer.Serialize(value, JsonOptions), Encoding.UTF8, "application/json");
    }

    private static string? ReadString(string json, string property)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string ReadAssistantText(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("parts", out var parts) || parts.ValueKind != JsonValueKind.Array)
            return string.Empty;
        return string.Join(
            "\n",
            parts.EnumerateArray()
                .Where(part => part.TryGetProperty("type", out var type) && type.GetString() == "text")
                .Select(part => part.TryGetProperty("text", out var text) ? text.GetString() : null)
                .OfType<string>());
    }

    private static string? ReadAssistantError(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("info", out var info)
            || !info.TryGetProperty("error", out var error)
            || error.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        return error.ValueKind == JsonValueKind.String ? error.GetString() : error.ToString();
    }

    private static string? ReadRunnerQuestion(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer))
            return null;
        foreach (var line in answer.Split('\n', StringSplitOptions.TrimEntries))
        {
            const string marker = "QUESTION_FOR_RUNNER:";
            if (!line.StartsWith(marker, StringComparison.Ordinal))
                continue;
            var question = line[marker.Length..].Trim();
            return string.IsNullOrWhiteSpace(question) ? null : question;
        }

        return null;
    }

    private static string? ReadFinish(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("info", out var info)
            && info.TryGetProperty("finish", out var nestedFinish)
            && nestedFinish.ValueKind == JsonValueKind.String)
            return nestedFinish.GetString();
        return document.RootElement.TryGetProperty("finish", out var finish) && finish.ValueKind == JsonValueKind.String
            ? finish.GetString()
            : null;
    }

    private static string SanitizeSessionId(string value)
    {
        var safe = new string(value.Where(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_').ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "session" : safe;
    }

    private static string RelativePath(string root, string path)
    {
        return Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string ResolveArtifactPath(string prompt, string artifacts)
    {
        return prompt.Replace("${ARTIFACTS_DIR}", artifacts.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.Ordinal);
    }

    private static string Sha256(string value)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static string Redact(string value, string credential)
    {
        return string.IsNullOrEmpty(credential) ? value : value.Replace(credential, "[REDACTED_LOCAL_CREDENTIAL]", StringComparison.Ordinal);
    }

    private sealed record OpenCodePromptResult(string Text, bool MaxTokensReached, bool AbortConfirmed, string? Error = null);

    private sealed record OpenCodeAbortResult(bool Confirmed, string? Error);

    private sealed class OpenCodePromptFailureException(string message) : Exception(message);
}
