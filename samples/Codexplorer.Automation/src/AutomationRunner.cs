using Codexplorer.Automation.Client;
using Codexplorer.Automation.Configuration;
using Codexplorer.Automation.Protocol;
using Codexplorer.Automation.Reporting;
using Codexplorer.Automation.Runner;
using Codexplorer.Automation.Scoring;
using System.Text;
using Codexplorer.Measurements;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Codexplorer.Automation;

/// <summary>Represents the manifest runner that finalizes reports after successful preflight validation.</summary>
internal sealed class AutomationRunner
{
    private readonly IAutomationProtocolTransport _transport;
    private readonly ICodexplorerAutomationClient _client;
    private readonly IAutomationTaskManifestLoader _taskManifestLoader;
    private readonly IRunnerHelperAi _helperAi;
    private readonly IRepositoryIdentityReader _repositoryIdentityReader;
    private readonly IReportAggregator _aggregator;
    private readonly IAnswerScorer _scorer;
    private readonly IRunReportWriter _writer;
    private readonly CodexplorerAutomationOptions _options;
    private readonly ILogger<AutomationRunner> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new instance of the <see cref="AutomationRunner" /> class.</summary>
    /// <param name="transport">The child process transport.</param>
    /// <param name="client">The typed protocol client.</param>
    /// <param name="taskManifestLoader">The immutable manifest loader.</param>
    /// <param name="helperAi">The helper model boundary.</param>
    /// <param name="repositoryIdentityReader">The checkout identity reader.</param>
    /// <param name="aggregator">The measurement report aggregator.</param>
    /// <param name="scorer">The deterministic answer scorer.</param>
    /// <param name="writer">The atomic report writer.</param>
    /// <param name="options">The runner configuration.</param>
    /// <param name="logger">The runner logger.</param>
    /// <param name="timeProvider">The clock that names the run folder, or <see langword="null" /> to use the system clock.</param>
    public AutomationRunner(
        IAutomationProtocolTransport transport, ICodexplorerAutomationClient client, IAutomationTaskManifestLoader taskManifestLoader,
        IRunnerHelperAi helperAi, IRepositoryIdentityReader repositoryIdentityReader, IReportAggregator aggregator, IRunReportWriter writer,
        IOptions<CodexplorerAutomationOptions> options, ILogger<AutomationRunner> logger, IAnswerScorer scorer, TimeProvider? timeProvider = null)
    {
        this._transport = transport;
        this._client = client;
        this._taskManifestLoader = taskManifestLoader;
        this._helperAi = helperAi;
        this._repositoryIdentityReader = repositoryIdentityReader;
        this._aggregator = aggregator;
        this._scorer = scorer;
        this._writer = writer;
        this._options = options.Value;
        this._logger = logger;
        this._timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Asynchronously runs tasks into a new run folder and writes the final report independently of work cancellation.</summary>
    /// <remarks>
    ///     The run folder is <c>&lt;OutputDirectory&gt;/&lt;runId&gt;</c>, where the run identifier is the UTC start time plus the arm.
    ///     It holds the report and one session directory per task, named by task identifier.
    /// </remarks>
    /// <param name="ct">The token observed while executing tasks.</param>
    /// <returns>
    ///     A task that represents the asynchronous operation. The task result contains zero for a completed run,
    ///     or one for failure, cancellation, budget exhaustion, or invalid collection.
    /// </returns>
    /// <exception cref="OptionsValidationException">The manifest fails preflight validation.</exception>
    /// <exception cref="InvalidOperationException">
    ///     The repository identity cannot be read.
    ///     -or-
    ///     The run folder already exists.
    /// </exception>
    /// <exception cref="OperationCanceledException">Cancellation is requested during repository preflight.</exception>
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var started = this._timeProvider.GetUtcNow();
        var manifest = this._taskManifestLoader.LoadSnapshot();
        var identity = await this._repositoryIdentityReader.ReadAsync(this._options.RepositoryPath, ct).ConfigureAwait(false);
        var runId = $"{started:yyyyMMdd-HHmmss}-{this._options.Arm}";
        var runFolder = Path.Combine(OutputPathResolver.Resolve(this._options.OutputDirectory, AppContext.BaseDirectory), runId);
        if (Directory.Exists(runFolder))
        {
            throw new InvalidOperationException($"Run folder '{runFolder}' already exists.");
        }

        Directory.CreateDirectory(runFolder);
        var taskReports = new List<TaskReport>();
        var settings = new EffectiveSettings();
        var partial = false;
        var exitCode = 0;
        TaskExecutionState? active = null;
        try
        {
            await this._transport.StartAsync(ct).ConfigureAwait(false);
            var ping = await this._client.PingAsync(ct).ConfigureAwait(false);
            settings = ping.Settings ?? throw new CodexplorerAutomationTransportException("Ping omitted effective settings.");
            foreach (var task in manifest.Tasks)
            {
                ct.ThrowIfCancellationRequested();
                active = new TaskExecutionState(task, this._options.GetTurnBudget(task.TaskSize), Path.Combine(runFolder, task.TaskId!));
                try
                {
                    await this.ExecuteTaskAsync(active, ct).ConfigureAwait(false);
                }
                catch (CodexplorerAutomationProtocolException)
                {
                    active.Outcome = ct.IsCancellationRequested ? "cancelled" : "failed";
                    active.ProtocolCompletion = false;
                    active.Measurements = active.Measurements with { Complete = false };
                    this._logger.LogWarning("Task {TaskId} failed with a protocol error.", task.TaskId);
                }
                catch (Exception ex) when (ex is not CodexplorerAutomationTransportException && ex is not OperationCanceledException)
                {
                    active.Outcome = ct.IsCancellationRequested ? "cancelled" : "failed";
                    active.ProtocolCompletion = false;
                    this._logger.LogWarning("Task {TaskId} failed ({FailureType}).", task.TaskId, ex.GetType().Name);
                }

                taskReports.Add(this.CreateTaskReport(active));
                partial |= active.Outcome is "failed" or "cancelled" || !active.Measurements.Complete;
                exitCode |= active.ProtocolCompletion && active.Measurements.Complete ? 0 : 1;
                var cancelled = active.Outcome == "cancelled";
                active = null;
                if (cancelled || ct.IsCancellationRequested)
                {
                    partial = true;
                    exitCode = 1;
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            partial = true;
            exitCode = 1;
            if (active is not null)
            {
                active.Outcome = "cancelled";
                active.ProtocolCompletion = false;
                active.Measurements = active.Measurements with { Complete = false };
            }
        }
        catch (Exception ex)
        {
            partial = true;
            exitCode = 1;
            if (active is not null)
            {
                active.Outcome = ct.IsCancellationRequested ? "cancelled" : "failed";
                active.ProtocolCompletion = false;
                active.Measurements = active.Measurements with { Complete = false };
            }

            this._logger.LogError("Automation run stopped ({FailureType}).", ex.GetType().Name);
        }
        finally
        {
            if (active is not null)
            {
                taskReports.Add(this.CreateTaskReport(active));
            }

            var completedIds = taskReports.Select(task => task.TaskId).ToHashSet(StringComparer.Ordinal);
            var unrun = manifest.Tasks.Select(task => task.TaskId!).Where(id => !completedIds.Contains(id)).ToArray();
            var metadata = new RunMetadata
            {
                CommitSha = identity.CommitSha,
                RepositoryDirty = identity.Dirty,
                RunId = runId,
                CaptureEnabled = this._options.Capture,
                StartedAtUtc = started,
                EndedAtUtc = this._timeProvider.GetUtcNow(),
                EffectiveSettings = settings,
                HelperModel = this._options.HelperAi.ModelName ?? "",
                HelperMaxOutputTokens = this._options.HelperAi.MaxOutputTokens,
                HelperTemperature = this._options.HelperAi.Temperature,
                TurnBudgets = this._options.TurnBudgets,
                Arm = this._options.Arm,
                ManifestPath = manifest.Provenance == "file" ? Path.GetRelativePath(runFolder, manifest.Path) : manifest.Path,
                ManifestSha256 = manifest.Sha256,
                ManifestProvenance = manifest.Provenance
            };
            try
            {
                var report = this._aggregator.CreateReport(metadata, taskReports, unrun, partial);
                await this._writer.WriteAsync(report, runFolder).ConfigureAwait(false);
                if (!report.Validation.IsValid)
                {
                    exitCode = 1;
                }
            }
            catch (Exception ex)
            {
                exitCode = 1;
                this._logger.LogError("Run report could not be written ({FailureType}).", ex.GetType().Name);
            }

            this._logger.LogInformation("Run folder: {RunFolder}", runFolder);
        }

        return exitCode;
    }

    /// <summary>Selects an exact path or one unique case-insensitive fallback from an ordinal inventory.</summary>
    /// <param name="requested">The manifest-relative path.</param>
    /// <param name="inventory">The non-null available relative paths.</param>
    /// <returns>The resolved path, or null for missing or ambiguous names.</returns>
    internal static string? SelectArtifactPath(string requested, IEnumerable<string> inventory)
    {
        var matches = inventory.Where(path => string.Equals(path, requested, StringComparison.OrdinalIgnoreCase)).ToArray();
        return matches.FirstOrDefault(path => path == requested) ?? (matches.Length == 1 ? matches[0] : null);
    }

    private async Task ExecuteTaskAsync(TaskExecutionState state, CancellationToken ct)
    {
        var opened = await this._client.OpenSessionAsync(new OpenSessionRequest
        {
            RepositoryUrl = state.Task.RepositoryUrl,
            ModelCallBudget = state.Budget.MaxTurns,
            WrapUpWindow = state.Budget.WrapUpWindow,
            SessionDirectory = state.SessionDirectory,
            Capture = this._options.Capture
        }, ct).ConfigureAwait(false);
        state.SessionId = opened.SessionId;
        state.ArtifactsAtStart = this.ListArtifacts(state.SessionDirectory).Select(file => file.Path).ToArray();
        state.WorkspacePath = opened.Workspace.LocalPath;
        var sessionOpen = true;
        try
        {
            ct.ThrowIfCancellationRequested();
            var message = state.Task.InitialPrompt!;
            if (state.Task.Probe is { } probe)
                message += "\n\n" + AutomationRunnerPrompts.CreateProbeInstruction(probe.Canary!);
            while (true)
            {
                var response = await this._client.SubmitAsync(new SubmitRequest(opened.SessionId, message), ct).ConfigureAwait(false);
                sessionOpen = response.SessionOpen;
                state.FinalAnswer = response.AssistantText;
                if (!state.WrapUpSent && state.Task.Probe is { } probeDefinition && response.AssistantText is { } original)
                    state.CanaryRepeated |= ValueMatcher.IsMatch(
                        TextNormalizer.Normalize(original), TextNormalizer.Normalize(probeDefinition.Canary!));
                state.Record(response);
                state.Outcome = response.Outcome;
                if (ct.IsCancellationRequested)
                {
                    state.Outcome = "cancelled";
                    return;
                }

                if (response.Outcome == "reply_received" && state.WrapUpSent)
                {
                    state.ProtocolCompletion = true;
                    return;
                }

                if (response.Outcome is "failed" or "cancelled" or "budget_exceeded" or "turn_budget_reached")
                {
                    return;
                }

                if (response.Outcome is not ("reply_received" or "max_turns_reached"))
                {
                    throw new CodexplorerAutomationTransportException("The submit outcome is unsupported.");
                }

                if (state.CallsRemaining == 0)
                {
                    state.Outcome = "turn_budget_reached";
                    return;
                }

                message = await this.BuildNextMessageAsync(state, response, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            if (sessionOpen)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var closed = await this._client.CloseSessionAsync(new CloseSessionRequest(opened.SessionId), cleanup.Token).ConfigureAwait(false);
                state.Measurements = closed.Measurements ?? state.Measurements with { Complete = false };
                if (closed.Status == "failed" || state.Measurements.SummaryCrossCheck == "mismatched")
                {
                    state.Outcome = ct.IsCancellationRequested ? "cancelled" : "failed";
                    state.ProtocolCompletion = false;
                }
            }
            else if (state.Measurements.SummaryCrossCheck == "mismatched")
            {
                state.Outcome = ct.IsCancellationRequested ? "cancelled" : "failed";
                state.ProtocolCompletion = false;
            }
        }
    }

    private async Task<string> BuildNextMessageAsync(TaskExecutionState state, SubmitResponse response, CancellationToken ct)
    {
        if (!state.WrapUpSent && state.CallsConsumed >= state.Budget.WrapUpTriggerTurns)
        {
            state.WrapUpSent = true;
            return AutomationRunnerPrompts.CreateWrapUpPrompt();
        }

        if (response.AsksRunner)
        {
            state.HelperCalls++;
            var helper = await this._helperAi.AnswerAsync(new RunnerHelperAiRequest(
                state.Task.TaskId!, state.Task.TaskSize, state.WorkspacePath!, state.Task.InitialPrompt!, response.RunnerQuestion!,
                response.AssistantText, state.CallsConsumed, state.Budget.MaxTurns, state.Budget.WrapUpWindow, state.WrapUpSent,
                state.Task.Probe?.Canary), ct)
                .ConfigureAwait(false);
            state.HelperResponses.Add(helper.Usage);
            if (string.IsNullOrWhiteSpace(helper.Answer))
            {
                throw new InvalidOperationException("The helper response was empty.");
            }

            return helper.Answer;
        }

        return response.Outcome == "max_turns_reached"
            ? AutomationRunnerPrompts.CreateResumePrompt(state.CallsRemaining)
            : AutomationRunnerPrompts.CreateContinuationPrompt(state.CallsRemaining);
    }

    private TaskReport CreateTaskReport(TaskExecutionState state)
    {
        this.WriteFinalAnswer(state);
        var inventory = this.ListArtifacts(state.SessionDirectory);
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var requested in (state.Task.Checks ?? []).Select(check => check.Artifact).OfType<string>().Distinct(StringComparer.Ordinal))
        {
            var selected = SelectArtifactPath(requested, inventory.Select(file => file.Path));
            if (selected is null)
                continue;
            try
            {
                texts[requested] = File.ReadAllText(Path.Combine(state.SessionDirectory, "artifacts", selected), Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                this._logger.LogWarning("Artifact for task {TaskId} could not be read ({FailureType}).", state.Task.TaskId, ex.GetType().Name);
            }
        }
        var scoring = this._scorer.Score(state.Task.Checks ?? [], state.Task.Probe, new ScoringInput
        {
            FinalAnswer = state.ProtocolCompletion && !string.IsNullOrWhiteSpace(state.FinalAnswer) ? state.FinalAnswer : null,
            ArtifactTexts = texts, CanaryRepeated = state.CanaryRepeated, Measurements = state.Measurements,
        });
        return this._aggregator.CreateTask(
            state.Task.TaskId!, state.Task.TaskSize.ToString().ToLowerInvariant(), state.Outcome, state.ProtocolCompletion, state.Budget.MaxTurns,
            state.Measurements, state.HelperResponses, state.HelperCalls, state.SessionId is null ? null : new TaskSessionRecord(
                state.SessionId, Path.GetFileName(state.SessionDirectory), state.ArtifactsAtStart, inventory), scoring);
    }

    /// <summary>Inventories artifacts while preserving finalization after enumeration or per-file failures.</summary>
    /// <param name="sessionDirectory">The absolute session directory.</param>
    /// <returns>The available files in ordinal path order.</returns>
    private ArtifactFileReport[] ListArtifacts(string sessionDirectory)
    {
        var root = Path.Combine(sessionDirectory, "artifacts");
        if (!Directory.Exists(root))
            return [];
        var files = new List<ArtifactFileReport>();
        try
        {
            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                try
                {
                    files.Add(new ArtifactFileReport
                    {
                        Path = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'), SizeBytes = new FileInfo(path).Length,
                    });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    this._logger.LogWarning("Artifact could not be inspected ({FailureType}).", ex.GetType().Name);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            this._logger.LogWarning("Artifact inventory could not be read ({FailureType}).", ex.GetType().Name);
            return [];
        }
        return files.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    ///     Writes the complete text of the reply that ended the task to <c>capture/final-answer.md</c> in its session directory.
    /// </summary>
    /// <remarks>Nothing is written when capture is off or the task ended without assistant text.</remarks>
    /// <param name="state">The finished task.</param>
    private void WriteFinalAnswer(TaskExecutionState state)
    {
        if (!this._options.Capture || string.IsNullOrWhiteSpace(state.FinalAnswer))
        {
            return;
        }

        try
        {
            var captureDirectory = Path.Combine(state.SessionDirectory, "capture");
            Directory.CreateDirectory(captureDirectory);
            File.WriteAllText(Path.Combine(captureDirectory, "final-answer.md"), state.FinalAnswer);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            this._logger.LogWarning("Final answer for task {TaskId} could not be written ({FailureType}).", state.Task.TaskId, ex.GetType().Name);
        }
    }

    private sealed class TaskExecutionState(AutomationTaskDefinition task, TurnBudgetProfile budget, string sessionDirectory)
    {
        /// <summary>Gets the immutable task definition.</summary>
        public AutomationTaskDefinition Task { get; } = task;
        /// <summary>Gets the model-call allowance and wrap-up window.</summary>
        public TurnBudgetProfile Budget { get; } = budget;
        /// <summary>Gets the absolute session directory of the task inside the run folder.</summary>
        public string SessionDirectory { get; } = sessionDirectory;
        public SessionMeasurements Measurements { get; set; } = new() { ModelCallBudget = budget.MaxTurns };
        /// <summary>Gets the received helper usage records.</summary>
        public List<UsageMeasurement> HelperResponses { get; } = [];
        /// <summary>Gets the attempted helper calls.</summary>
        public long HelperCalls { get; set; }
        /// <summary>Gets the opened workspace path.</summary>
        public string? WorkspacePath { get; set; }
        /// <summary>Gets Codexplorer's session identifier once the session opened.</summary>
        public string? SessionId { get; set; }
        /// <summary>Gets the files found in the artifacts folder when the session opened.</summary>
        public IReadOnlyList<string> ArtifactsAtStart { get; set; } = [];
        /// <summary>Gets the assistant text of the latest submit response.</summary>
        public string? FinalAnswer { get; set; }
        /// <summary>Gets the task terminal outcome.</summary>
        public string Outcome { get; set; } = "failed";
        /// <summary>Gets whether an in-budget wrap-up reply was received.</summary>
        public bool ProtocolCompletion { get; set; }
        /// <summary>Gets whether the wrap-up prompt was submitted.</summary>
        public bool WrapUpSent { get; set; }
        /// <summary>Gets whether the original assistant text repeated the code before wrap-up.</summary>
        public bool CanaryRepeated { get; set; }
        /// <summary>Gets the cumulative attempted model-call count.</summary>
        public int CallsConsumed { get; private set; }
        /// <summary>Gets the model calls still available.</summary>
        public int CallsRemaining => Math.Max(0, this.Budget.MaxTurns - this.CallsConsumed);

        /// <summary>Replaces the cumulative snapshot after a submit response.</summary>
        /// <param name="response">The received submit response.</param>
        public void Record(SubmitResponse response)
        {
            if (response.Measurements is not null)
            {
                this.Measurements = response.Measurements;
                this.CallsConsumed = this.Measurements.ProviderCalls.Count;
            }
            else
            {
                this.Measurements = this.Measurements with { Complete = false };
                this.CallsConsumed += Math.Max(0, response.ModelTurnsCompleted);
            }
        }
    }
}
