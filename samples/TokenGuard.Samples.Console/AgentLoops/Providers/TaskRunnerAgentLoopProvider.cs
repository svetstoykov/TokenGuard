using System.Diagnostics;
using TokenGuard.Benchmark.AgentWorkflow.Tasks;
using TokenGuard.Core.Contexts;
using TokenGuard.Core.Enums;
using TokenGuard.Core.Models;
using TokenGuard.Core.Models.Content;
using TokenGuard.Core.Options;
using TokenGuard.Core.Strategies;
using TokenGuard.Core.TokenCounting;
using TokenGuard.Tools.Tools;

namespace TokenGuard.Samples.Console.AgentLoops.Providers;

internal sealed class TaskRunnerAgentLoopProvider : IAgentLoopProvider, IDisposable
{
    private readonly AgentLoopTaskDefinition _task;
    private readonly IAgentLoopProvider _innerProvider;
    private readonly SessionLogger _logger;
    private readonly ConversationContext _conversationContext;
    private readonly string _workspaceDirectory;
    private readonly Dictionary<string, ITool> _toolMap;
    private readonly Stopwatch _stopwatch;

    private int _totalCompactionCount;
    private bool _disposed;

    public TaskRunnerAgentLoopProvider(
        AgentLoopTaskDefinition task,
        IAgentLoopProvider innerProvider,
        string workspaceDirectory,
        IEnumerable<ITool> tools)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(innerProvider);
        ArgumentNullException.ThrowIfNull(workspaceDirectory);
        ArgumentNullException.ThrowIfNull(tools);

        this._task = task;
        this._innerProvider = innerProvider;
        this._workspaceDirectory = workspaceDirectory;
        this._logger = new SessionLogger();
        this._toolMap = tools.ToDictionary(t => t.Name, t => t);
        this._stopwatch = new Stopwatch();

        var budget = ContextBudget.For(maxTokens: 10000);
        var counter = new EstimatedTokenCounter();
        var strategy = new SlidingWindowStrategy(new SlidingWindowOptions(windowSize: 10));
        this._conversationContext = new ConversationContext(budget, counter, strategy);

        this.Name = $"TaskRunner[{task.Name}]";
        this.ModelId = innerProvider.ModelId;

        this._logger.LogBudgetInfo(budget, nameof(SlidingWindowStrategy));
    }

    public string Name { get; }

    public string ModelId { get; }

    public async Task<ProviderTurnResult> ExecuteTurnAsync(
        IReadOnlyList<ContextMessage> preparedMessages,
        IReadOnlyList<ITool> tools,
        CancellationToken cancellationToken = default)
    {
        this.ThrowIfDisposed();

        return await this._innerProvider.ExecuteTurnAsync(preparedMessages, tools, cancellationToken);
    }

    public async Task<TaskRunResult> RunTaskAsync(CancellationToken cancellationToken = default)
    {
        this.ThrowIfDisposed();

        this._stopwatch.Start();
        this._totalCompactionCount = 0;

        await this._logger.LogTaskStartAsync(this._task);

        await this._task.SeedWorkspaceAsync(this._workspaceDirectory);
        await this._logger.LogWorkspaceSeededAsync(this._workspaceDirectory);

        this._conversationContext.SetSystemPrompt(this._task.SystemPrompt);
        this._logger.LogMessageAdded(this._conversationContext.History.First(), "System Prompt Set");

        this._conversationContext.AddUserMessage(this._task.UserMessage);
        this._logger.LogMessageAdded(this._conversationContext.History.Last(), "Task User Message Added");

        var turnCount = 0;
        string? finalResponse = null;

        while (turnCount < 50)
        {
            turnCount++;

            this._logger.LogTurnStart(turnCount);

            this._logger.LogHistoryBeforePrepare(this._conversationContext.History);

            var preparedMessages = await this._conversationContext.PrepareAsync(cancellationToken);
            this._logger.LogPreparedMessages(preparedMessages, this._conversationContext.Budget);

            if (preparedMessages.Any(m => m.State != CompactionState.Original))
            {
                this._totalCompactionCount++;
            }

            ProviderTurnResult turnResult;
            try
            {
                turnResult = await this.ExecuteTurnAsync(preparedMessages, this._toolMap.Values.ToList(), cancellationToken);
            }
            catch (Exception ex)
            {
                this._logger.LogError($"{this._innerProvider.Name} turn execution", ex);
                return TaskRunResult.Failure(ex.Message, turnCount, this._stopwatch.Elapsed);
            }

            this._conversationContext.RecordModelResponse(turnResult.ResponseSegments, turnResult.InputTokens);

            var responseKind = turnResult.HasToolCalls
                ? $"Tool Calls [{string.Join(", ", turnResult.ToolCalls.Select(call => call.ToolName))}]"
                : "Text Response";
            this._logger.LogModelResponse(this._conversationContext.History.Last(), turnResult.InputTokens, responseKind);

            if (turnResult.HasToolCalls)
            {
                foreach (var call in turnResult.ToolCalls)
                {
                    var resultText = this._toolMap.TryGetValue(call.ToolName, out var tool)
                        ? tool.Execute(call.ArgumentsJson)
                        : $"Error: Unknown tool '{call.ToolName}'.";

                    this._conversationContext.RecordToolResult(call.ToolCallId, call.ToolName, resultText);
                    this._logger.LogToolResultRecorded(this._conversationContext.History.Last());
                }

                continue;
            }

            finalResponse = string.Concat(turnResult.ResponseSegments.OfType<TextContent>().Select(t => t.Content));

            if (finalResponse.Contains(this._task.CompletionMarker, StringComparison.Ordinal))
            {
                break;
            }

            this._conversationContext.AddUserMessage(
                "Task not complete. Continue working. " +
                $"Respond with '{this._task.CompletionMarker}' when done.");
            this._logger.LogMessageAdded(this._conversationContext.History.Last(), "Continuation Prompt");
        }

        this._stopwatch.Stop();

        var finalTokenCount = this._conversationContext.History.Sum(m => m.TokenCount ?? 0);
        this._logger.LogSessionSummary(
            this._conversationContext.History.Count,
            this._totalCompactionCount,
            this._stopwatch.Elapsed,
            finalTokenCount);

        try
        {
            await this._task.AssertOutcomeAsync(this._workspaceDirectory, finalResponse);
            this._logger.LogAssertionPassed();
        }
        catch (Exception ex)
        {
            this._logger.LogAssertionFailed(ex);
            return TaskRunResult.Failure($"Assertion failed: {ex.Message}", turnCount, this._stopwatch.Elapsed);
        }

        return TaskRunResult.Success(turnCount, this._stopwatch.Elapsed, finalResponse, this._logger.LogFilePath);
    }

    public void Dispose()
    {
        if (this._disposed)
        {
            return;
        }

        this._logger.Dispose();
        this._disposed = true;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(this._disposed, this);
    }
}

internal sealed record TaskRunResult(
    bool Success,
    int TurnCount,
    TimeSpan Duration,
    string? FinalResponse,
    string? LogFilePath,
    string? ErrorMessage)
{
    public static TaskRunResult Success(int turnCount, TimeSpan duration, string? finalResponse, string logFilePath)
        => new(true, turnCount, duration, finalResponse, logFilePath, null);

    public static TaskRunResult Failure(string errorMessage, int turnCount, TimeSpan duration)
        => new(false, turnCount, duration, null, null, errorMessage);
}

internal static class SessionLoggerExtensions
{
    public static Task LogTaskStartAsync(this SessionLogger logger, AgentLoopTaskDefinition task)
    {
        logger.GetType()
            .GetMethod("WriteSection", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.Invoke(logger, ["Task Definition", new[]
            {
                $"- Name: `{task.Name}`",
                $"- Conversation: `{task.ConversationName}`",
                $"- Size: `{task.Size}`",
                $"- Completion Marker: `{task.CompletionMarker}`",
            }]);

        return Task.CompletedTask;
    }

    public static Task LogWorkspaceSeededAsync(this SessionLogger logger, string workspaceDirectory)
    {
        logger.GetType()
            .GetMethod("WriteSection", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.Invoke(logger, ["Workspace", new[]
            {
                $"- Directory: `{workspaceDirectory}`",
                $"- Seeded: `true`",
            }]);

        return Task.CompletedTask;
    }

    public static void LogTurnStart(this SessionLogger logger, int turnNumber)
    {
        logger.GetType()
            .GetMethod("WriteSection", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.Invoke(logger, [$"Turn {turnNumber}", Array.Empty<string>()]);
    }

    public static void LogAssertionPassed(this SessionLogger logger)
    {
        logger.GetType()
            .GetMethod("WriteSection", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.Invoke(logger, ["Assertion Result", new[] { "- Status: `PASSED`" }]);
    }

    public static void LogAssertionFailed(this SessionLogger logger, Exception ex)
    {
        logger.GetType()
            .GetMethod("WriteSection", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.Invoke(logger, ["Assertion Result", new[]
            {
                $"- Status: `FAILED`",
                $"- Error: `{ex.Message}`",
            }]);
    }
}
