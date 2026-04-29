using Codexplorer.Automation.Client;
using Codexplorer.Automation.Configuration;
using Codexplorer.Automation.Protocol;
using Codexplorer.Automation.Runner;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Codexplorer.Automation;

internal sealed class AutomationRunner
{
    private readonly IAutomationProtocolTransport _transport;
    private readonly ICodexplorerAutomationClient _client;
    private readonly IAutomationTaskManifestLoader _taskManifestLoader;
    private readonly IRunnerHelperAi _helperAi;
    private readonly CodexplorerAutomationOptions _options;
    private readonly ILogger<AutomationRunner> _logger;

    public AutomationRunner(
        IAutomationProtocolTransport transport,
        ICodexplorerAutomationClient client,
        IAutomationTaskManifestLoader taskManifestLoader,
        IRunnerHelperAi helperAi,
        IOptions<CodexplorerAutomationOptions> options,
        ILogger<AutomationRunner> logger)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(taskManifestLoader);
        ArgumentNullException.ThrowIfNull(helperAi);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        this._transport = transport;
        this._client = client;
        this._taskManifestLoader = taskManifestLoader;
        this._helperAi = helperAi;
        this._options = options.Value;
        this._logger = logger;
    }

    public async Task<int> RunAsync(CancellationToken ct)
    {
        try
        {
            this._logger.LogInformation("Automation runner starting.");
            await this._transport.StartAsync(ct).ConfigureAwait(false);

            var ping = await this._client.PingAsync(ct).ConfigureAwait(false);
            this._logger.LogInformation(
                "Connected to Codexplorer automation protocol v{ProtocolVersion} using process {ProcessId}.",
                ping.ProtocolVersion,
                this._transport.ProcessId);

            var tasks = this._taskManifestLoader.LoadTasks();
            var encounteredFailure = false;

            this._logger.LogInformation("Loaded {TaskCount} automation tasks.", tasks.Count);

            foreach (var task in tasks)
            {
                var taskResult = await this.ExecuteTaskAsync(task, ct).ConfigureAwait(false);
                if (taskResult.Succeeded)
                {
                    this._logger.LogInformation("Task {TaskId} completed successfully.", taskResult.TaskId);
                }
                else
                {
                    this._logger.LogWarning(
                        "Task {TaskId} stopped before success. Reason: {Reason}",
                        taskResult.TaskId,
                        taskResult.Reason);
                }

                encounteredFailure |= !taskResult.Succeeded;
            }

            this._logger.LogInformation(
                "Automation runner finished. EncounteredFailure={EncounteredFailure}.",
                encounteredFailure);
            return encounteredFailure ? 1 : 0;
        }
        catch (CodexplorerAutomationProtocolException ex)
        {
            this._logger.LogError(
                ex,
                "Codexplorer returned protocol error {ErrorCode} for request {RequestId}.",
                ex.Code,
                ex.RequestId);
            return 1;
        }
        catch (CodexplorerProcessExitedException ex)
        {
            this._logger.LogError(
                ex,
                "Codexplorer process exited unexpectedly with code {ExitCode}.",
                ex.ExitCode);
            return 1;
        }
        catch (CodexplorerAutomationTransportException ex)
        {
            this._logger.LogError(ex, "Codexplorer automation transport failed.");
            return 1;
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Automation runner failed unexpectedly.");
            return 1;
        }
    }

    private async Task<TaskExecutionResult> ExecuteTaskAsync(AutomationTaskDefinition task, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(task);

        var budget = this._options.GetTurnBudget(task.TaskSize);
        this._logger.LogInformation(
            "Starting task {TaskId} ({TaskTitle}). Repository={RepositoryUrl}. Budget={MaxTurns} turns with wrap-up window {WrapUpWindow}.",
            task.TaskId,
            task.Title,
            task.RepositoryUrl,
            budget.MaxTurns,
            budget.WrapUpWindow);
        var openedSession = await this._client
            .OpenSessionAsync(CreateOpenSessionRequest(task), ct)
            .ConfigureAwait(false);
        var taskState = new TaskExecutionState(task, openedSession.Workspace.LocalPath, budget);

        this._logger.LogInformation(
            "Opened automation session {SessionId} for task {TaskId} ({TaskTitle}) in workspace {WorkspacePath}. SessionLogPath={SessionLogPath}.",
            openedSession.SessionId,
            task.TaskId,
            task.Title,
            openedSession.Workspace.LocalPath,
            openedSession.LogFilePath);

        var sessionOpen = true;

        try
        {
            var nextMessage = task.InitialPrompt!;

            while (true)
            {
                var response = await this._client
                    .SubmitAsync(new SubmitRequest(openedSession.SessionId, nextMessage), ct)
                    .ConfigureAwait(false);

                sessionOpen = response.SessionOpen;
                taskState.Record(response.ModelTurnsCompleted);

                this._logger.LogInformation(
                    "Task {TaskId} received outcome {Outcome} after {ExchangeTurns} turns ({TotalTurns}/{MaxTurns} total). SessionOpen={SessionOpen}.",
                    task.TaskId,
                    response.Outcome,
                    response.ModelTurnsCompleted,
                    taskState.TurnsConsumed,
                    budget.MaxTurns,
                    response.SessionOpen);

                switch (response.Outcome)
                {
                    case "reply_received":
                        if (taskState.WrapUpSent)
                        {
                            return TaskExecutionResult.Success(task.TaskId!);
                        }

                        nextMessage = await this.BuildNextMessageAsync(taskState, response, ct).ConfigureAwait(false);
                        continue;

                    case "max_turns_reached":
                        nextMessage = await this.BuildNextMessageAsync(taskState, response, ct).ConfigureAwait(false);
                        continue;

                    case "degraded":
                        this._logger.LogWarning(
                            "Task {TaskId} degraded. Reason: {Reason}",
                            task.TaskId,
                            response.DegradationReason);
                        if (sessionOpen)
                        {
                            await this.CloseSessionAsync(openedSession.SessionId, task.TaskId!, ct).ConfigureAwait(false);
                            sessionOpen = false;
                        }

                        return TaskExecutionResult.Failure(
                            task.TaskId!,
                            response.DegradationReason ?? "Codexplorer degraded the exchange and could not continue safely.");

                    case "failed":
                        this._logger.LogWarning(
                            "Task {TaskId} failed. FailureType={FailureType}. Message={FailureMessage}",
                            task.TaskId,
                            response.Failure?.ExceptionType,
                            response.Failure?.Message);
                        if (sessionOpen)
                        {
                            await this.CloseSessionAsync(openedSession.SessionId, task.TaskId!, ct).ConfigureAwait(false);
                            sessionOpen = false;
                        }

                        return TaskExecutionResult.Failure(
                            task.TaskId!,
                            response.Failure?.Message ?? "Codexplorer reported a failed exchange.");

                    case "cancelled":
                        this._logger.LogWarning("Task {TaskId} was cancelled by Codexplorer.", task.TaskId);
                        if (sessionOpen)
                        {
                            await this.CloseSessionAsync(openedSession.SessionId, task.TaskId!, ct).ConfigureAwait(false);
                            sessionOpen = false;
                        }

                        return TaskExecutionResult.Failure(task.TaskId!, "Codexplorer cancelled the exchange.");

                    default:
                        throw new InvalidOperationException(
                            $"Task '{task.TaskId}' received unsupported submit outcome '{response.Outcome}'.");
                }
            }
        }
        finally
        {
            if (sessionOpen)
            {
                await this.CloseSessionAsync(openedSession.SessionId, task.TaskId!, ct).ConfigureAwait(false);
            }
        }
    }

    private static OpenSessionRequest CreateOpenSessionRequest(AutomationTaskDefinition task)
    {
        ArgumentNullException.ThrowIfNull(task);

        return new OpenSessionRequest
        {
            RepositoryUrl = task.RepositoryUrl
        };
    }

    private async Task<string> BuildNextMessageAsync(
        TaskExecutionState taskState,
        SubmitResponse response,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(taskState);
        ArgumentNullException.ThrowIfNull(response);

        if (response.AsksRunner)
        {
            this._logger.LogInformation(
                "Task {TaskId} requested helper clarification: {RunnerQuestion}",
                taskState.Task.TaskId,
                response.RunnerQuestion);

            return await this._helperAi
                .AnswerAsync(
                    new RunnerHelperAiRequest(
                        taskState.Task.TaskId!,
                        taskState.Task.TaskSize,
                        taskState.WorkspacePath,
                        taskState.Task.InitialPrompt!,
                        response.RunnerQuestion!,
                        response.AssistantText,
                        taskState.TurnsConsumed,
                        taskState.Budget.MaxTurns,
                        taskState.Budget.WrapUpWindow,
                        taskState.WrapUpSent),
                    ct)
                .ConfigureAwait(false);
        }

        if (taskState.ShouldSendWrapUp)
        {
            taskState.MarkWrapUpSent();
            this._logger.LogInformation(
                "Task {TaskId} reached wrap-up window at {TurnsConsumed}/{MaxTurns} turns. Sending wrap-up prompt.",
                taskState.Task.TaskId,
                taskState.TurnsConsumed,
                taskState.Budget.MaxTurns);
            return AutomationRunnerPrompts.CreateWrapUpPrompt();
        }

        var continuationMessage = response.Outcome == "max_turns_reached"
            ? "resume"
            : "continue";
        this._logger.LogInformation(
            "Task {TaskId} will {ContinuationMode}. PlannedTurnsRemaining={TurnsRemaining}.",
            taskState.Task.TaskId,
            continuationMessage,
            taskState.TurnsRemaining);

        return response.Outcome == "max_turns_reached"
            ? AutomationRunnerPrompts.CreateResumePrompt(taskState.TurnsRemaining)
            : AutomationRunnerPrompts.CreateContinuationPrompt(taskState.TurnsRemaining);
    }

    private async Task CloseSessionAsync(string sessionId, string taskId, CancellationToken ct)
    {
        var closedSession = await this._client
            .CloseSessionAsync(new CloseSessionRequest(sessionId), ct)
            .ConfigureAwait(false);

        this._logger.LogInformation(
            "Closed automation session {SessionId} for task {TaskId} with status {Status}.",
            closedSession.SessionId,
            taskId,
            closedSession.Status);
    }

    private sealed class TaskExecutionState
    {
        public TaskExecutionState(
            AutomationTaskDefinition task,
            string workspacePath,
            TurnBudgetProfile budget)
        {
            ArgumentNullException.ThrowIfNull(task);
            ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
            ArgumentNullException.ThrowIfNull(budget);

            this.Task = task;
            this.WorkspacePath = workspacePath;
            this.Budget = budget;
        }

        public AutomationTaskDefinition Task { get; }

        public string WorkspacePath { get; }

        public TurnBudgetProfile Budget { get; }

        public int TurnsConsumed { get; private set; }

        public int TurnsRemaining => Math.Max(0, this.Budget.MaxTurns - this.TurnsConsumed);

        public bool WrapUpSent { get; private set; }

        public bool ShouldSendWrapUp => !this.WrapUpSent && this.TurnsConsumed >= this.Budget.WrapUpTriggerTurns;

        public void MarkWrapUpSent()
        {
            this.WrapUpSent = true;
        }

        public void Record(int exchangeTurns)
        {
            this.TurnsConsumed += Math.Max(0, exchangeTurns);
        }
    }

    private sealed record TaskExecutionResult(string TaskId, bool Succeeded, string? Reason)
    {
        public static TaskExecutionResult Success(string taskId) => new(taskId, true, null);

        public static TaskExecutionResult Failure(string taskId, string reason) => new(taskId, false, reason);
    }
}
