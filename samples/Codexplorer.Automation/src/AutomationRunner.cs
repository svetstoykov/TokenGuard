using Codexplorer.Automation.Client;
using Codexplorer.Automation.Configuration;
using Codexplorer.Automation.Protocol;
using Codexplorer.Automation.Reporting;
using Codexplorer.Automation.Runner;
using Codexplorer.Measurements;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Codexplorer.Automation;

/// <summary>Runs manifest tasks and always finalizes a partial or complete measurement report.</summary>
internal sealed class AutomationRunner
{
    private readonly IAutomationProtocolTransport _transport;
    private readonly ICodexplorerAutomationClient _client;
    private readonly IAutomationTaskManifestLoader _taskManifestLoader;
    private readonly IRunnerHelperAi _helperAi;
    private readonly IRepositoryIdentityReader _repositoryIdentityReader;
    private readonly IReportAggregator _aggregator;
    private readonly IRunReportWriter _writer;
    private readonly CodexplorerAutomationOptions _options;
    private readonly ILogger<AutomationRunner> _logger;

    /// <summary>Initializes a new instance of the <see cref="AutomationRunner" /> class.</summary>
    /// <param name="transport">The child process transport.</param>
    /// <param name="client">The typed protocol client.</param>
    /// <param name="taskManifestLoader">The immutable manifest loader.</param>
    /// <param name="helperAi">The helper model boundary.</param>
    /// <param name="repositoryIdentityReader">The checkout identity reader.</param>
    /// <param name="aggregator">The measurement report aggregator.</param>
    /// <param name="writer">The atomic report writer.</param>
    /// <param name="options">The runner configuration.</param>
    /// <param name="logger">The runner logger.</param>
    public AutomationRunner(
        IAutomationProtocolTransport transport, ICodexplorerAutomationClient client, IAutomationTaskManifestLoader taskManifestLoader,
        IRunnerHelperAi helperAi, IRepositoryIdentityReader repositoryIdentityReader, IReportAggregator aggregator, IRunReportWriter writer,
        IOptions<CodexplorerAutomationOptions> options, ILogger<AutomationRunner> logger)
    {
        this._transport = transport;
        this._client = client;
        this._taskManifestLoader = taskManifestLoader;
        this._helperAi = helperAi;
        this._repositoryIdentityReader = repositoryIdentityReader;
        this._aggregator = aggregator;
        this._writer = writer;
        this._options = options.Value;
        this._logger = logger;
    }

    /// <summary>Asynchronously runs tasks and writes the final report independently of work cancellation.</summary>
    /// <param name="ct">The token observed while executing tasks.</param>
    /// <returns>A task containing zero for a valid completed run, or one for failure, cancellation, or invalid collection.</returns>
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var started = DateTimeOffset.UtcNow;
        var taskReports = new List<TaskReport>();
        AutomationManifestSnapshot? manifest = null;
        var identity = new RepositoryIdentity("unavailable", false);
        var settings = new EffectiveSettings();
        var partial = false;
        var exitCode = 0;
        TaskExecutionState? active = null;
        try
        {
            manifest = this._taskManifestLoader.LoadSnapshot();
            identity = await this._repositoryIdentityReader.ReadAsync(this._options.RepositoryPath, ct).ConfigureAwait(false);
            await this._transport.StartAsync(ct).ConfigureAwait(false);
            var ping = await this._client.PingAsync(ct).ConfigureAwait(false);
            settings = ping.Settings ?? throw new CodexplorerAutomationTransportException("Ping omitted effective settings.");
            foreach (var task in manifest.Tasks)
            {
                ct.ThrowIfCancellationRequested();
                active = new TaskExecutionState(task, this._options.GetTurnBudget(task.TaskSize));
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
                partial |= !active.ProtocolCompletion || !active.Measurements.Complete;
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
            var unrun = manifest?.Tasks.Select(task => task.TaskId!).Where(id => !completedIds.Contains(id)).ToArray() ?? [];
            var metadata = new RunMetadata
            {
                CommitSha = identity.CommitSha,
                RepositoryDirty = identity.Dirty,
                StartedAtUtc = started,
                EndedAtUtc = DateTimeOffset.UtcNow,
                EffectiveSettings = settings,
                HelperModel = this._options.HelperAi.ModelName ?? "",
                HelperMaxOutputTokens = this._options.HelperAi.MaxOutputTokens,
                HelperTemperature = this._options.HelperAi.Temperature,
                TurnBudgets = this._options.TurnBudgets,
                Arm = this._options.Arm,
                ManifestPath = manifest?.Path ?? this._options.ManifestPath ?? "inline",
                ManifestSha256 = manifest?.Sha256 ?? "unavailable",
                ManifestProvenance = manifest?.Provenance ?? "unavailable"
            };
            try
            {
                var report = this._aggregator.CreateReport(metadata, taskReports, unrun, partial);
                var outputDirectory = Path.GetFullPath(this._options.OutputDirectory, AppContext.BaseDirectory);
                await this._writer.WriteAsync(report, outputDirectory).ConfigureAwait(false);
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
        }

        return exitCode;
    }

    private async Task ExecuteTaskAsync(TaskExecutionState state, CancellationToken ct)
    {
        var opened = await this._client.OpenSessionAsync(new OpenSessionRequest
        {
            RepositoryUrl = state.Task.RepositoryUrl,
            ModelCallBudget = state.Budget.MaxTurns,
            WrapUpWindow = state.Budget.WrapUpWindow
        }, ct).ConfigureAwait(false);
        state.SessionLogPath = opened.LogFilePath;
        state.WorkspacePath = opened.Workspace.LocalPath;
        var sessionOpen = true;
        try
        {
            ct.ThrowIfCancellationRequested();
            var message = state.Task.InitialPrompt!;
            while (true)
            {
                var response = await this._client.SubmitAsync(new SubmitRequest(opened.SessionId, message), ct).ConfigureAwait(false);
                sessionOpen = response.SessionOpen;
                state.SessionLogPath = response.LogFilePath;
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
                response.AssistantText, state.CallsConsumed, state.Budget.MaxTurns, state.Budget.WrapUpWindow, state.WrapUpSent), ct)
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

    private TaskReport CreateTaskReport(TaskExecutionState state) => this._aggregator.CreateTask(
        state.Task.TaskId!, state.Task.TaskSize.ToString().ToLowerInvariant(), state.Outcome, state.ProtocolCompletion, state.Budget.MaxTurns,
        state.Measurements, state.HelperResponses, state.HelperCalls, state.SessionLogPath);

    private sealed class TaskExecutionState(AutomationTaskDefinition task, TurnBudgetProfile budget)
    {
        /// <summary>Gets the immutable task definition.</summary>
        public AutomationTaskDefinition Task { get; } = task;
        /// <summary>Gets the model-call allowance and wrap-up window.</summary>
        public TurnBudgetProfile Budget { get; } = budget;
        public SessionMeasurements Measurements { get; set; } = new() { ModelCallBudget = budget.MaxTurns };
        /// <summary>Gets the received helper usage records.</summary>
        public List<UsageMeasurement> HelperResponses { get; } = [];
        /// <summary>Gets the attempted helper calls.</summary>
        public long HelperCalls { get; set; }
        /// <summary>Gets the opened workspace path.</summary>
        public string? WorkspacePath { get; set; }
        /// <summary>Gets the session transcript path.</summary>
        public string? SessionLogPath { get; set; }
        /// <summary>Gets the task terminal outcome.</summary>
        public string Outcome { get; set; } = "failed";
        /// <summary>Gets whether an in-budget wrap-up reply was received.</summary>
        public bool ProtocolCompletion { get; set; }
        /// <summary>Gets whether the wrap-up prompt was submitted.</summary>
        public bool WrapUpSent { get; set; }
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
